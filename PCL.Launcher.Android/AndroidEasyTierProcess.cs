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
        // 启动过程要等 VPN 授权与 tun 建立，放到后台线程，避免卡住界面。
        _ = Task.Run(LaunchCoreAsync);
        return 0;
    }

    private async Task LaunchCoreAsync()
    {
        if (await TryLaunchJniAsync().ConfigureAwait(false))
            return;

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
                ipv4 = "10.126.126.2";

            // tun 需要 VpnService：先同步建立（阻塞在后台线程上等授权结果）
            var fd = await EnsureVpnFdAsync().ConfigureAwait(false);
            var config = AndroidEasyTierJni.BuildConfig(_options, instanceName, ipv4);
            LogWrapper.Info("EasyTier", "JNI 启动配置：" + config.Replace(Environment.NewLine, " "));

            var run = AndroidEasyTierJni.RunNetworkInstance(config);
            if (run != 0)
            {
                LogWrapper.Warn("EasyTier",
                    $"JNI runNetworkInstance 失败：{run}，{AndroidEasyTierJni.GetLastError()}");
                return false;
            }

            if (fd >= 0)
            {
                var set = AndroidEasyTierJni.SetTunFd(instanceName, fd);
                LogWrapper.Info("EasyTier", $"JNI setTunFd(instance={instanceName}, fd={fd}) => {set}");
            }
            else
            {
                LogWrapper.Warn("EasyTier", "没有可用的 tun fd，JNI 实例将以无 tun 方式运行");
            }

            return true;
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "EasyTier", "JNI 启动失败，回退到 CLI");
            return false;
        }
    }

    /// <summary>确保 VpnService 建立并返回 tun fd（-1 表示失败）。</summary>
    private async Task<int> EnsureVpnFdAsync()
    {
        try
        {
            var activity = AndroidPermissions.Activity;
            var granted = await AndroidVpnController.EnsurePreparedAsync(activity).ConfigureAwait(false);
            if (!granted) return -1;

            AndroidVpnController.Start();
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(200).ConfigureAwait(false);
                if (EasyTierVpnService.Fd >= 0) return EasyTierVpnService.Fd;
            }

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
    public Task<string?> QueryPeersAsync() => RunCliAsync("peer list");

    /// <summary>轮询 peer 表，拿到第一个可用虚拟 IPv4 后建立端口转发。</summary>
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
