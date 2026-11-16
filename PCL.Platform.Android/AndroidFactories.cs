using PCL.Core.Platform;
using PCL.Core.UI;
using PCL.Platform;

namespace PCL.Platform.Android;

/// <summary>Android 的平台与 UI 服务装配入口。</summary>
public static class AndroidFactories
{
    /// <summary>组装 Android 平台模块。不可用的能力全部退化为显式空实现。</summary>
    public static PlatformModule CreatePlatform()
    {
        return new PlatformModule
        {
            Kind = PlatformKind.Android,
            Capabilities = PlatformCapabilities.Android,
            SystemProxy = new AndroidSystemProxyProvider(),
            WindowInterop = new AndroidWindowInterop(),
            Shortcuts = new AndroidShortcutService(),
            Java = new AndroidJavaPlatformInfo(),
            ProcessCleanup = new AndroidProcessCleanupService(),
            GpuPreference = new UnsupportedGpuPreferenceService(),
            LaunchEnvironment = new UnsupportedLaunchEnvironmentService(),
            SystemFacts = new AndroidSystemFacts(),
            Hardware = new AndroidHardwareInfo(),
            Shell = new AndroidShellLauncher(),
            Region = new AndroidRegionSignalProvider(),
            RecycleBin = new AndroidRecycleBinService(),
            FatalDialog = new AndroidFatalDialogService(),
            KeyProtectorFactory = AndroidKeyProtector.Create
        };
    }

    /// <summary>组装 Android 的 Avalonia UI 服务：文件对话框走系统选择器，通知走通知栏。</summary>
    public static UiModule CreateUi()
    {
        var dispatcher = new AvaloniaUiDispatcher();

        return new UiModule
        {
            Dispatcher = dispatcher,
            Clipboard = new ClipboardService(),
            FileDialogs = new FileDialogService(),
            ResourceStore = new ResourceStore(),
            Localization = new LocalizationBackend(dispatcher),
            Theme = new ThemeBackend(),
            SystemTheme = new AndroidSystemThemeProvider(),
            Notifications = new AndroidNotificationService()
        };
    }

    /// <summary>一次性初始化应用运行时。</summary>
    public static void Register()
    {
        if (ApplicationRuntime.IsInitialized) return;
        ApplicationRuntime.Initialize(new ApplicationServices
        {
            Platform = CreatePlatform(),
            Ui = CreateUi()
        });
    }
}
