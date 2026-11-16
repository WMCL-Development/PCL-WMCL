using PCL.Core.App;

namespace PCL.Core.Link.EasyTier;

/// <summary>
///     从配置读取 EasyTier 的通用开关。配置系统在非常早期（或单测环境）可能尚未初始化，
///     此时回退到默认值，保证联机代码本身不因配置未就绪而抛异常。
/// </summary>
public static class EasyTierDefaults
{
    public static bool DisableHolePunching => Safe(() => !Config.Link.TryPunchSym, false);

    public static bool DisableIpv6 => Safe(() => !Config.Link.EnableIPv6, false);

    public static bool LatencyFirst => Safe(() => Config.Link.UseLatencyFirstMode, false);

    public static string DefaultProtocol =>
        Safe(() => Config.Link.ProtocolPreference.ToString().ToLowerInvariant(), "tcp");

    /// <summary>EasyTier 是否已安装；平台模块未初始化时按“未安装”处理。</summary>
    public static bool IsInstalled =>
        Safe(() => Scaffolding.EasyTier.EasyTierMetadata.IsInstalled, false);

    private static T Safe<T>(System.Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (System.Exception exception) when (
            exception is System.InvalidOperationException or System.TypeInitializationException)
        {
            return fallback;
        }
    }
}
