using PCL.Core.App.IoC;
using PCL.Core.Logging;
using PCL.Core.UI.Animation;
using PCL.Platform.Android;
using PCL.UI;
using PCL.UI.Bridges;

namespace PCL.Android;

/// <summary>
///     Android 的最小启动序列：只初始化页面真正需要的服务。
///     桌面端的 IoC 生命周期包含窗口、命名管道、单实例等桌面专属服务，这里不启用。
/// </summary>
public static class AndroidRuntimeSetup
{
    private static bool _done;

    public static AndroidApplicationHost? Host { get; private set; }

    public static void Initialize()
    {
        if (_done) return;
        _done = true;

        Step("platform-ui", AndroidComposition.Initialize);
        Step("bootstrap", AndroidBootstrap.Initialize);
        // 日志服务在桌面由 IoC 生命周期启动；Android 没有完整的生命周期，这里手动初始化，
        // 否则「查看日志」页面拿不到 Logger，日志也不会落盘到 Paths.Data/Log。
        Step("logging", LogService.InitializeStandalone);
        Step("animation", AnimationRegistration.Register);
        Step("dialogs", () =>
        {
            DialogPresenter.Install();
            HintPresenter.Install();
        });
        Step("localization", InitLocalization);

        Step("easytier", AndroidEasyTierProcess.Register);
        Step("notification-channel", AndroidNotifications.EnsureChannel);
        Step("permissions", AndroidPermissions.RequestStartupPermissions);

        // 添加已有游戏文件夹需要按路径访问共享存储，缺权限时引导用户开启。
        Step("storage-permission", () =>
        {
            if (AndroidStoragePermission.HasAllFilesAccess()) return;
            AndroidStoragePermission.RequestAllFilesAccess();
        });

        // 主题服务在桌面属于 WindowCreating 阶段，这里手动跑一遍，
        // 否则 ColorBrush* 等资源为空：标题栏白底白字、控件配色缺失。
        Step("theme", () =>
        {
            PCL.Core.UI.Theme.ThemeService.RefreshColorMode();
            PCL.Core.UI.Theme.ThemeService.ApplyColorResources();
            PCL.Core.UI.Theme.ThemeService.ApplyGrayResources();
        });

        AndroidBootstrap.Breadcrumb(
            $"SETUP_DONE runtime={PCL.Core.Platform.ApplicationRuntime.IsInitialized}");
    }

    /// <summary>
    ///     设置应用宿主（供资源回退查找）并加载语言资源，
    ///     这样 <c>Lang.Text</c> 与 avares 资源无需桌面生命周期即可工作。
    /// </summary>
    private static void InitLocalization()
    {
        Host = new AndroidApplicationHost();
        Lifecycle.CurrentApplication = Host;

        // 先加载基础语言，保证首帧中文案可用；用户配置的语言在外壳首帧后应用。
        PCL.Core.Platform.ApplicationRuntime.Current.Ui.Localization
            .ApplyLanguageResources(
                PCL.Core.App.Localization.LocalizationService.DefaultLanguageCode,
                null);
    }

    /// <summary>Activity 销毁时结束宿主循环。</summary>
    public static void Shutdown()
    {
        LogService.ShutdownStandalone();
        Host?.Shutdown();
    }

    private static void Step(string name, Action action)
    {
        AndroidBootstrap.Breadcrumb($"STEP_BEGIN {name}");
        try
        {
            action();
            AndroidBootstrap.Breadcrumb($"STEP_OK {name}");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("STEP_FAIL " + name, exception);
            LogWrapper.Error(exception, "Android", $"启动步骤失败：{name}");
        }
    }
}
