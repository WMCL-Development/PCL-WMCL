using System.Text;
using System.Text.RegularExpressions;
using PCL.Core.Link.EasyTier;
using PCL.Core.Logging;

namespace PCL.Android;

/// <summary>
///     Android 上的 EasyTier 实现：执行内置的 aarch64 静态二进制。
///     二进制以 lib*.so 形式随 APK 打进 lib/arm64-v8a，安装后位于 nativeLibraryDir，
///     只有该目录下的文件才有执行权限（应用数据目录是 noexec）。
///     没有 tun 权限，所以「连进游戏」依赖端口转发：先读到房主虚拟 IP，
///     再用 easytier-cli 把本地端口转发到房主的 25565。
/// </summary>
public sealed partial class AndroidEasyTierProcess : IEasyTierProcess
{
    private const string CoreName = "libeasytier-core.so";
    private const string CliName = "libeasytier-cli.so";
    private const int DefaultRpcPort = 27_000;

    private readonly EasyTierOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private Java.Lang.Process? _process;
    private volatile bool _stopped;
    private int _rpcPort = DefaultRpcPort;

    public AndroidEasyTierProcess(EasyTierOptions options)
    {
        _options = options;
    }

    private static string NativeDir =>
        global::Android.App.Application.Context.ApplicationInfo!.NativeLibraryDir!;

    private static string CorePath => Path.Combine(NativeDir, CoreName);

    private static string CliPath => Path.Combine(NativeDir, CliName);

    public int Launch()
    {
        // 同步入口无法等待 VPN 授权，后台启动并返回 0；需要真实结果请用 LaunchAsync。
        _ = Task.Run(LaunchCoreAsync);
        return 0;
    }

    /// <summary>等待真正启动完成并返回结果（0 成功）。</summary>
    public async Task<int> LaunchAsync()
    {
        await LaunchCoreAsync().ConfigureAwait(false);
        return _launchResult;
    }

    private int _launchResult = 1;

    private async Task LaunchCoreAsync()
    {
        EasyTierDiagnostics.Reset();
        if (await TryLaunchJniAsync().ConfigureAwait(false))
        {
            _launchResult = 0;
            return;
        }

        try
        {
            if (!File.Exists(CorePath))
            {
                LogWrapper.Error("EasyTier", "找不到内置 EasyTier 核心：" + CorePath);
                return;
            }

            var arguments = EasyTierArguments.Build(_options, _rpcPort);
            var command = $"\"{CorePath}\" {arguments}";
            LogWrapper.Info("EasyTier", "启动 EasyTier：" + command);

            _process = Java.Lang.Runtime.GetRuntime()!.Exec(["/system/bin/sh", "-c", command]);

            // 建立 tun（当前仅验证授权与 fd 获取，核心还没接收 fd）
            _ = EnsureVpnAsync();
            _ = PumpOutputAsync(_process);

            // 加入方：等网络建立后自动把本地端口转发到房主的 25565。
            if (!_options.AsHost && _options.PortForwards.Count == 0 && _options.AutoForwardLocalPort is > 0)
                _ = AutoForwardAsync(_options.AutoForwardLocalPort.Value, _options.AutoForwardRemotePort);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "EasyTier", "启动 EasyTier 失败");
            _launchResult = 1;
        }
    }

    /// <summary>用官方 JNI 模块启动，并把 VpnService 的 tun fd 交给它。</summary>
    private async Task<bool> TryLaunchJniAsync()
    {
        try
        {
            var instanceName = "pcl-" + (_options.NetworkName ?? "net") + "-" + _rpcPort;
            // 有 tun 才能拿到虚拟 IP：房主用固定地址，加入方用同网段另一个地址。
            var ipv4 = _options.VirtualIp;
            if (string.IsNullOrWhiteSpace(ipv4) && !_options.NoTun)
                // 官方 Astral 客户端默认会占用 10.126.126.1 / 10.126.126.2，
                // 加入方固定用 .3 可以避免和它撞地址。
                ipv4 = _options.AsHost ? "10.126.126.1" : "10.126.126.3";
            EasyTierVpnService.RequestedIpv4 = ipv4 ?? "10.126.126.2";

            // tun 需要 VpnService：先同步建立（阻塞在后台线程上等授权结果）
            var fd = await EnsureVpnFdAsync().ConfigureAwait(false);
            var config = AndroidEasyTierJni.BuildConfig(_options, instanceName, ipv4);
            LogWrapper.Info("EasyTier", "JNI 启动配置：" + config.Replace(Environment.NewLine, " "));

            EasyTierDiagnostics.Append($"JNI 可用：{AndroidEasyTierJni.IsAvailable}");
            EasyTierDiagnostics.Append("实际配置：" + Environment.NewLine + MaskSecret(config));
            EasyTierDiagnostics.Append($"虚拟 IP：{ipv4 ?? "(无)"}，tun fd：{(fd >= 0 ? fd.ToString() : "未建立")}");

            // 上一轮留下的实例会让 run_network_instance 直接失败，先全部清掉
            var cleared = AndroidEasyTierJni.StopAllInstances();
            EasyTierDiagnostics.Append($"清理旧实例 => {cleared}");

            var parse = AndroidEasyTierJni.ParseConfig(config);
            EasyTierDiagnostics.Append($"parse_config => {parse}" +
                (parse == 0 ? "" : "（" + AndroidEasyTierJni.GetLastError() + "）"));
            if (parse != 0)
            {
                LogWrapper.Warn("EasyTier", "parse_config 失败：" + AndroidEasyTierJni.GetLastError());
                return false;
            }

            var run = AndroidEasyTierJni.RunNetworkInstance(config);
            if (run != 0)
            {
                var error = AndroidEasyTierJni.GetLastError();
                LogWrapper.Warn("EasyTier", $"run_network_instance 失败：{run}，{error}");
                EasyTierDiagnostics.Append($"run_network_instance 失败：{run}，{error}");
                return false;
            }

            EasyTierDiagnostics.Append("run_network_instance 成功");

            if (fd >= 0)
            {
                var set = AndroidEasyTierJni.SetTunFd(instanceName, fd);
                LogWrapper.Info("EasyTier", $"JNI setTunFd(instance={instanceName}, fd={fd}) => {set}");
                EasyTierDiagnostics.Append($"setTunFd => {set}" +
                    (set == 0 ? "" : "（" + AndroidEasyTierJni.GetLastError() + "）"));
            }
            else
            {
                LogWrapper.Warn("EasyTier", "没有可用的 tun fd，JNI 实例将以无 tun 方式运行");
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(6000).ConfigureAwait(false);
                var infos = AndroidEasyTierJni.CollectNetworkInfos();
                EasyTierDiagnostics.Append(infos.Length == 0 ? "实例信息：空" : "实例信息：" + Environment.NewLine + infos);
                WriteDiagnosticsFile();

                // 路由是经中继慢慢收敛的，30 秒后再采样一次
                await Task.Delay(24000).ConfigureAwait(false);
                var later = AndroidEasyTierJni.CollectNetworkInfos();
                EasyTierDiagnostics.Append("30 秒后实例信息：" + Environment.NewLine + later);
                WriteDiagnosticsFile();
            });

            return true;
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "EasyTier", "JNI 启动失败，回退到 CLI");
            EasyTierDiagnostics.Append("JNI 异常：" + exception.Message + "（已回退 CLI）");
            return false;
        }
    }

    private bool _peerTableLogged;

    /// <summary>日志/诊断里隐藏网络密钥。</summary>
    private static string MaskSecret(string config)
    {
        var parts = config.Split((char)10);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!parts[i].StartsWith("network_secret", StringComparison.Ordinal)) continue;
            var index = parts[i].IndexOf((char)61);
            if (index < 0 || parts[i].Length <= index + 5) continue;
            parts[i] = parts[i].Substring(0, index + 4) + "***";
        }
        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>把诊断写到共享存储，方便在电脑上直接查看。</summary>
    public static void WriteDiagnosticsFile()
    {
        try
        {
            const string directory = "/sdcard/Download/DSH-Go";
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "easytier-diag.txt");
            System.IO.File.WriteAllText(path,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                EasyTierDiagnostics.Text + Environment.NewLine +
                "--- LastError ---" + Environment.NewLine +
                AndroidBootstrap.LastError + Environment.NewLine +
                "--- Breadcrumbs ---" + Environment.NewLine +
                AndroidBootstrap.RecentBreadcrumbs);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "EasyTier", "写诊断文件失败");
        }
    }

    /// <summary>确保 VpnService 建立并返回 tun fd（-1 表示失败）。</summary>
    private async Task<int> EnsureVpnFdAsync()
    {
        try
        {
            var activity = AndroidPermissions.Activity;
            var granted = await AndroidVpnController.EnsurePreparedAsync(activity).ConfigureAwait(false);
            if (!granted)
            {
                EasyTierDiagnostics.Append("VPN 授权失败（请在弹出的系统对话框里点「确定」）");
                return -1;
            }

            AndroidVpnController.Start();
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(200).ConfigureAwait(false);
                if (EasyTierVpnService.Fd >= 0) return EasyTierVpnService.Fd;
            }

            EasyTierDiagnostics.Append("VPN 服务已启动但 8 秒内没拿到 tun fd");
            return -1;
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "EasyTier", "建立 tun 失败");
            return -1;
        }
    }

    private async Task EnsureVpnAsync()
    {
        try
        {
            var activity = AndroidPermissions.Activity;
            var granted = await AndroidVpnController.EnsurePreparedAsync(activity).ConfigureAwait(false);
            if (!granted)
            {
                LogWrapper.Warn("EasyTier", "用户未授权 VPN，跳过 tun 建立");
                return;
            }

            // 授权是异步的：期间用户可能已经点了「断开」，这时不能再起服务。
            if (_stopped) return;

            AndroidVpnController.Start();
            await Task.Delay(1200).ConfigureAwait(false);

            if (_stopped)
            {
                AndroidEasyTierJni.StopAllInstances();
            AndroidVpnController.Stop();
                return;
            }

            LogWrapper.Info("EasyTier", "tun fd=" + EasyTierVpnService.Fd);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "EasyTier", "建立 tun 失败");
        }
    }

    public Task<int> StopAsync()
    {
        try
        {
            _stopped = true;
            _cts.Cancel();
            _process?.Destroy();
            _process = null;

            // 断开时一并关掉 tun，否则系统状态栏的 VPN 图标会一直留着。
            AndroidEasyTierJni.StopAllInstances();
            AndroidVpnController.Stop();
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "EasyTier", "停止 EasyTier 失败");
            return Task.FromResult(1);
        }
    }

    /// <summary>通过 easytier-cli 读取 peer 表。</summary>
    /// <summary>
    ///     返回与 easytier-cli peer 相同格式的 Markdown 表格，
    ///     数据来自 JNI 库的实例信息（routes 字段），CLI 没在跑时成员列表也能显示。
    /// </summary>
    public Task<string?> QueryPeersAsync()
    {
        try
        {
            var raw = AndroidEasyTierJni.CollectNetworkInfos();
            if (string.IsNullOrWhiteSpace(raw)) return Task.FromResult<string?>(null);

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("| ipv4 | hostname | cost | lat(ms) | loss | rx | tx | tunnel | NAT | version |");
            builder.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

            foreach (var line in raw.Split((char)10))
            {
                var brace = line.IndexOf('{');
                if (brace < 0) continue;
                var end2 = line.LastIndexOf('}');
                if (end2 <= brace) continue;

                using var document = System.Text.Json.JsonDocument.Parse(line[brace..(end2 + 1)]);
                var root = document.RootElement;

                // 本机
                if (root.TryGetProperty("my_node_info", out var mine) &&
                    mine.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var selfIp = "";
                    // 无 tun 时 virtual_ipv4 为 null，不能直接再取子属性
                    if (mine.TryGetProperty("virtual_ipv4", out var v4) &&
                        v4.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        v4.TryGetProperty("address", out var addr) && addr.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        addr.TryGetProperty("addr", out var num) && num.ValueKind == System.Text.Json.JsonValueKind.Number)
                        selfIp = FormatIpv4(num.GetUInt32());
                    var selfName = mine.TryGetProperty("hostname", out var host) ? host.GetString() : "本机";
                    builder.AppendLine($"| {selfIp} | {selfName} | Local | - | - | - | - | - | - | 本机 |");
                }

                // 已建立的 peer（含中继与多跳）
                if (root.TryGetProperty("peers", out var peers))
                    foreach (var peer in peers.EnumerateArray())
                        AppendPeer(builder, peer);

                // 路由表里的节点（多跳时只有这里有 ipv4）
                if (root.TryGetProperty("routes", out var routes))
                    foreach (var route in routes.EnumerateArray())
                        AppendPeer(builder, route);
            }

            try
            {
                System.IO.File.AppendAllText("/sdcard/Download/DSH-Go/easytier-peers.log",
                    DateTime.Now.ToString("HH:mm:ss") + " 成员表：" + Environment.NewLine + builder + Environment.NewLine);
            }
            catch
            {
                // 共享存储不可写时忽略
            }

            return Task.FromResult<string?>(builder.ToString());
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "EasyTier", "生成 peer 表失败");
            try
            {
                System.IO.File.AppendAllText("/sdcard/Download/DSH-Go/easytier-peers.log",
                    DateTime.Now.ToString("HH:mm:ss") + " 生成失败：" + exception + Environment.NewLine);
            }
            catch
            {
                // 忽略
            }
            return Task.FromResult<string?>(null);
        }
    }

    private static void AppendPeer(System.Text.StringBuilder builder, System.Text.Json.JsonElement peer)
    {
        var hostname = peer.TryGetProperty("hostname", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String
            ? name.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(hostname)) return;

        var costText = "peer";
        if (peer.TryGetProperty("cost", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Number)
            costText = c.GetInt32() == 1 ? "p2p" : $"relay({c.GetInt32()})";

        // path_latency 单位是毫秒，不要再换算
        var latencyText = "-";
        if (peer.TryGetProperty("path_latency", out var lat) && lat.ValueKind == System.Text.Json.JsonValueKind.Number)
            latencyText = lat.GetInt32().ToString();
        else if (peer.TryGetProperty("latency", out var lat2) && lat2.ValueKind == System.Text.Json.JsonValueKind.Number)
            latencyText = lat2.GetInt32().ToString();

        var ip = ReadIpv4(peer);
        if (string.IsNullOrWhiteSpace(ip)) ip = "-";

        var tunnel = "";
        if (peer.TryGetProperty("tunnel_proto", out var tp) && tp.ValueKind == System.Text.Json.JsonValueKind.String)
            tunnel = tp.GetString() ?? "";

        builder.AppendLine($"| {ip} | {hostname} | {costText} | {latencyText} | 0% | 0 | 0 | {tunnel} | - | - |");
    }

    /// <summary>ipv4_addr 既可能是字符串，也可能是 { address: { addr }, network_length } 对象。</summary>
    private static string ReadIpv4(System.Text.Json.JsonElement peer)
    {
        if (!peer.TryGetProperty("ipv4_addr", out var addr)) return "";
        if (addr.ValueKind == System.Text.Json.JsonValueKind.String) return addr.GetString() ?? "";
        if (addr.ValueKind != System.Text.Json.JsonValueKind.Object) return "";
        if (!addr.TryGetProperty("address", out var inner) ||
            !inner.TryGetProperty("addr", out var num) ||
            num.ValueKind != System.Text.Json.JsonValueKind.Number)
            return "";
        var value = num.GetUInt32();
        var text = $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
        if (addr.TryGetProperty("network_length", out var len) && len.ValueKind == System.Text.Json.JsonValueKind.Number)
            text += "/" + len.GetInt32();
        return text;
    }

    private static string FormatIpv4(uint value)
    {
        return $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
    }

    private async Task AutoForwardAsync(int localPort, int remotePort)
    {
        try
        {
            for (var attempt = 0; attempt < 40 && !_cts.IsCancellationRequested; attempt++)
            {
                await Task.Delay(1500, _cts.Token).ConfigureAwait(false);
                var table = await RunCliAsync("peer list").ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(table)) continue;

                var ip = FindPeerIpv4(table);
                if (ip is null) continue;

                var rule = $"tcp://127.0.0.1:{localPort}/{ip}:{remotePort}";
                var result = await RunCliAsync($"port-forward add tcp 127.0.0.1:{localPort} {ip}:{remotePort}")
                    .ConfigureAwait(false);
                LogWrapper.Info("EasyTier", $"已添加端口转发 {rule}：{result?.Trim()}");
                return;
            }

            LogWrapper.Warn("EasyTier", "未能自动建立端口转发：没读到房主虚拟 IP");
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "EasyTier", "自动端口转发失败");
        }
    }

    /// <summary>从 peer 表里取第一个形如 10.x.x.x / 192.168.x.x 的虚拟 IPv4。</summary>
    private static string? FindPeerIpv4(string table)
    {
        foreach (Match match in Ipv4Regex().Matches(table))
        {
            var value = match.Value;
            if (value.StartsWith("127.") || value.StartsWith("0.")) continue;
            return value;
        }

        return null;
    }

    private async Task<string?> RunCliAsync(string arguments)
    {
        try
        {
            if (!File.Exists(CliPath)) return null;

            var command = $"\"{CliPath}\" -p 127.0.0.1:{_rpcPort} {arguments}";
            var process = Java.Lang.Runtime.GetRuntime()!.Exec(["/system/bin/sh", "-c", command]);
            using var reader = new StreamReader(process.InputStream!, Encoding.UTF8);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
            process.WaitFor();
            return text;
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "EasyTier", "执行 easytier-cli 失败：" + arguments);
            return null;
        }
    }

    private static async Task PumpOutputAsync(Java.Lang.Process process)
    {
        try
        {
            using var reader = new StreamReader(process.InputStream!, Encoding.UTF8);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                LogWrapper.Debug("EasyTier", line);
        }
        catch
        {
            // 进程结束时流会关闭，忽略。
        }
    }

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex Ipv4Regex();

    /// <summary>平台注册入口：Android 启动时调用一次。</summary>
    public static void Register()
    {
        EasyTierProcessFactory.Register(options => new AndroidEasyTierProcess(options));
    }
}
