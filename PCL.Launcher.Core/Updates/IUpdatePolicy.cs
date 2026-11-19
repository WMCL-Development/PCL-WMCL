using PCL.Core.Platform;

namespace PCL;

public interface IUpdatePolicy
{
    bool SupportsInAppUpdate { get; }
    string InstallButtonKey { get; }
}

public static class UpdatePolicies
{
    private static IUpdatePolicy? _current;

    public static IUpdatePolicy Current => _current ??= Create(PlatformRuntime.Current.Kind);

    private static IUpdatePolicy Create(PlatformKind kind)
    {
        return kind switch
        {
            PlatformKind.Windows => new WindowsUpdatePolicy(),
            // 其余平台（Linux/macOS/Android）没有应用内安装能力，一律走手动下载。
            _ => new ManualUpdatePolicy()
        };
    }
}

internal sealed class WindowsUpdatePolicy : IUpdatePolicy
{
    public bool SupportsInAppUpdate => true;
    public string InstallButtonKey => "Setup.Update.Install";
}

internal sealed class ManualUpdatePolicy : IUpdatePolicy
{
    public bool SupportsInAppUpdate => false;
    public string InstallButtonKey => "Setup.Update.OpenDownload";
}