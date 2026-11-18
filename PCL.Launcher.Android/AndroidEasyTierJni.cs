using System.Runtime.InteropServices;
using Android.Runtime;
using PCL.Core.Link.EasyTier;
using PCL.Core.Logging;

namespace PCL.Android;

/// <summary>
///     官方 EasyTier Android JNI 模块的托管封装（easytier-contrib/easytier-android-jni）。
///     与 CLI 版的关键差别：这里可以把 VpnService 建立的 tun fd 交给 EasyTier，
///     从而获得虚拟 IP 与完整的网络能力。
/// </summary>
internal static class AndroidEasyTierJni
{
    private const string Library = "easytier_android_jni";

    [DllImport(Library, EntryPoint = "Java_com_easytier_jni_EasyTierJNI_parseConfig")]
    private static extern int ParseConfigNative(IntPtr env, IntPtr clazz, IntPtr config);

    [DllImport(Library, EntryPoint = "Java_com_easytier_jni_EasyTierJNI_runNetworkInstance")]
    private static extern int RunNetworkInstanceNative(IntPtr env, IntPtr clazz, IntPtr config);

    [DllImport(Library, EntryPoint = "Java_com_easytier_jni_EasyTierJNI_setTunFd")]
    private static extern int SetTunFdNative(IntPtr env, IntPtr clazz, IntPtr instanceName, int fd);

    [DllImport(Library, EntryPoint = "Java_com_easytier_jni_EasyTierJNI_collectNetworkInfos")]
    private static extern IntPtr CollectNetworkInfosNative(IntPtr env, IntPtr clazz);

    [DllImport(Library, EntryPoint = "Java_com_easytier_jni_EasyTierJNI_getLastError")]
    private static extern IntPtr GetLastErrorNative(IntPtr env, IntPtr clazz);

    /// <summary>库是否可用（首次调用会触发 dlopen）。</summary>
    public static bool IsAvailable { get; private set; } = true;

    public static int ParseConfig(string toml) => CallWithString(ParseConfigNative, toml);

    public static int RunNetworkInstance(string toml) => CallWithString(RunNetworkInstanceNative, toml);

    public static int SetTunFd(string instanceName, int fd)
    {
        try
        {
            var env = JNIEnv.Handle;
            using var name = new Java.Lang.String(instanceName);
            return SetTunFdNative(env, IntPtr.Zero, name.Handle, fd);
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            LogWrapper.Error(exception, "EasyTier", "setTunFd 调用失败");
            return -1;
        }
    }

    public static string? GetLastError()
    {
        try
        {
            var env = JNIEnv.Handle;
            var handle = GetLastErrorNative(env, IntPtr.Zero);
            if (handle == IntPtr.Zero) return null;
            var text = Java.Lang.Object.GetObject<Java.Lang.String>(
                handle, JniHandleOwnership.DoNotTransfer);
            return text?.ToString();
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "EasyTier", "读取 JNI 错误信息失败");
            return null;
        }
    }

    private static int CallWithString(Func<IntPtr, IntPtr, IntPtr, int> call, string value)
    {
        try
        {
            var env = JNIEnv.Handle;
            using var text = new Java.Lang.String(value);
            return call(env, IntPtr.Zero, text.Handle);
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            LogWrapper.Error(exception, "EasyTier", "EasyTier JNI 调用失败");
            return -1;
        }
    }

    /// <summary>把启动参数转成 JNI 模块要求的 TOML。</summary>
    public static string BuildConfig(EasyTierOptions options, string instanceName, string? ipv4)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"inst_name = \"{instanceName}\"");
        builder.AppendLine($"network = \"{options.NetworkName}\"");
        if (!string.IsNullOrWhiteSpace(options.NetworkSecret))
            builder.AppendLine($"network_secret = \"{options.NetworkSecret}\"");
        if (!string.IsNullOrWhiteSpace(ipv4))
            builder.AppendLine($"ipv4 = \"{ipv4}\"");
        if (!string.IsNullOrWhiteSpace(options.Hostname))
            builder.AppendLine($"hostname = \"{options.Hostname}\"");
        builder.AppendLine($"latency_first = {(options.LatencyFirst ? "true" : "false")}");
        builder.AppendLine($"disable_p2p = {(options.P2POnly ? "false" : "true")}");
        builder.AppendLine("enable_ipv4 = true");
        builder.AppendLine("dhcp = false");
        builder.AppendLine("mtu = 1380");
        builder.AppendLine("log_level = \"info\"");

        var peers = options.Peers
            .Where(peer => !string.IsNullOrWhiteSpace(peer))
            .Select(peer => $"\"{peer}\"")
            .ToList();
        if (peers.Count > 0)
            builder.AppendLine($"peers = [{string.Join(", ", peers)}]");

        return builder.ToString();
    }
}
