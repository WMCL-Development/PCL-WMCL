using System.Runtime.InteropServices;
using System.Text;
using PCL.Core.Link.EasyTier;
using PCL.Core.Logging;

namespace PCL.Android;

/// <summary>
///     官方 EasyTier Android 库的托管封装。
///     库除了 JNI 入口外还导出了纯 C 接口（parse_config / run_network_instance / set_tun_fd / ...），
///     直接用 DllImport 调用即可，不需要构造 JNIEnv 和 Java 字符串。
/// </summary>
internal static class AndroidEasyTierJni
{
    private const string Library = "easytier_ffi";

    [DllImport(Library, EntryPoint = "parse_config")]
    private static extern int ParseConfigNative(IntPtr config);

    [DllImport(Library, EntryPoint = "run_network_instance")]
    private static extern int RunNetworkInstanceNative(IntPtr config);

    [DllImport(Library, EntryPoint = "retain_network_instance")]
    private static extern int RetainNetworkInstanceNative(IntPtr[]? names, UIntPtr length);

    [DllImport(Library, EntryPoint = "set_tun_fd")]
    private static extern int SetTunFdNative(IntPtr instanceName, int fd);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyValuePair
    {
        public IntPtr Key;
        public IntPtr Value;
    }

    [DllImport(Library, EntryPoint = "collect_network_infos")]
    private static extern int CollectNetworkInfosNative([Out] KeyValuePair[] infos, UIntPtr maxLength);

    [DllImport(Library, EntryPoint = "get_error_msg")]
    private static extern void GetErrorMsgNative(out IntPtr error);

    [DllImport(Library, EntryPoint = "free_string")]
    private static extern void FreeStringNative(IntPtr value);

    /// <summary>库是否可用（调用失败时置 false）。</summary>
    public static bool IsAvailable { get; private set; } = true;

    public static int ParseConfig(string toml) => WithUtf8(toml, ParseConfigNative);

    public static int RunNetworkInstance(string toml) => WithUtf8(toml, RunNetworkInstanceNative);

    /// <summary>读取各实例的运行信息（JSON），用于界面诊断。</summary>
    public static string CollectNetworkInfos(int max = 4)
    {
        try
        {
            var array = new KeyValuePair[max];
            var count = CollectNetworkInfosNative(array, (UIntPtr)max);
            if (count <= 0) return "";
            var builder = new StringBuilder();
            for (var i = 0; i < count && i < max; i++)
            {
                var key = Marshal.PtrToStringUTF8(array[i].Key) ?? "?";
                var value = Marshal.PtrToStringUTF8(array[i].Value) ?? "";
                builder.AppendLine(key + ": " + value);
            }
            return builder.ToString();
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "EasyTier", "读取实例信息失败");
            return "";
        }
    }

    /// <summary>停止所有实例；重复开房前必须先清掉旧实例，否则会报 instance already exists。</summary>
    public static int StopAllInstances()
    {
        try
        {
            return RetainNetworkInstanceNative(null, UIntPtr.Zero);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "EasyTier", "清空 EasyTier 实例失败");
            return -1;
        }
    }

    public static int SetTunFd(string instanceName, int fd) => WithUtf8(instanceName, ptr => SetTunFdNative(ptr, fd));

    /// <summary>读取库记录的最近一次错误（没有则返回空）。</summary>
    public static string GetLastError()
    {
        try
        {
            GetErrorMsgNative(out var pointer);
            if (pointer == IntPtr.Zero) return "";
            var text = Marshal.PtrToStringUTF8(pointer) ?? "";
            FreeStringNative(pointer);
            return text;
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "EasyTier", "读取 EasyTier 错误信息失败");
            return "";
        }
    }

    private static int WithUtf8(string value, Func<IntPtr, int> call)
    {
        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.StringToCoTaskMemUTF8(value);
            return call(pointer);
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            LogWrapper.Error(exception, "EasyTier", "调用 EasyTier 原生接口失败");
            return -1;
        }
        finally
        {
            if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>
    ///     生成官方库使用的 TOML 配置。
    ///     字段名必须与 EasyTier 的 <c>TomlConfigLoader</c> 一致：
    ///     顶层是 instance_name / network_name / network_secret / ipv4 / dhcp，
    ///     节点写成 <c>[[peer]] uri = "..."</c>，开关放在 <c>[flags]</c> 表里。
    /// </summary>
    public static string BuildConfig(EasyTierOptions options, string instanceName, string? ipv4)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"instance_name = \"{Escape(instanceName)}\"");
        if (!string.IsNullOrWhiteSpace(ipv4))
            builder.AppendLine($"ipv4 = \"{ipv4}\"");
        if (!string.IsNullOrWhiteSpace(options.Hostname))
            builder.AppendLine($"hostname = \"{Escape(options.Hostname)}\"");
        builder.AppendLine("dhcp = false");
        // 打开本地 RPC 门户，方便在电脑上用 easytier-cli 直接查询实例状态
        builder.AppendLine("rpc_portal = \"127.0.0.1:27000\"");

        var listeners = options.Listeners
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => $"\"{Escape(item)}\"")
            .ToList();
        if (listeners.Count > 0)
            builder.AppendLine($"listeners = [{string.Join(", ", listeners)}]");

        foreach (var peer in options.Peers.Where(peer => !string.IsNullOrWhiteSpace(peer)))
        {
            builder.AppendLine("[[peer]]");
            builder.AppendLine($"uri = \"{Escape(peer)}\"");
        }

        foreach (var forward in options.PortForwards.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            // 形如 tcp://127.0.0.1:25565/10.126.126.1:25565
            var parts = forward.Split("://", 2);
            if (parts.Length != 2) continue;
            var proto = parts[0];
            var addresses = parts[1].Split('/', 2);
            if (addresses.Length != 2) continue;
            builder.AppendLine("[[port_forward]]");
            builder.AppendLine($"bind_addr = \"{Escape(addresses[0])}\"");
            builder.AppendLine($"dst_addr = \"{Escape(addresses[1])}\"");
            builder.AppendLine($"proto = \"{Escape(proto)}\"");
        }

        builder.AppendLine("[network_identity]");
        builder.AppendLine($"network_name = \"{Escape(options.NetworkName)}\"");
        if (!string.IsNullOrWhiteSpace(options.NetworkSecret))
            builder.AppendLine($"network_secret = \"{Escape(options.NetworkSecret)}\"");
        
        builder.AppendLine("[flags]");
        builder.AppendLine($"no_tun = {(options.NoTun ? "true" : "false")}");
        builder.AppendLine($"latency_first = {(options.LatencyFirst ? "true" : "false")}");
        builder.AppendLine("disable_p2p = false");
        builder.AppendLine("enable_encryption = true");
        builder.AppendLine("multi_thread = true");
        builder.AppendLine("mtu = 1380");
        return builder.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
