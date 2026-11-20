using System.Reflection;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;
using PCL.Core.App.IoC;
using PCL.Core.App.Localization;
using PCL.Platform.Android;
using PCL.UI.Controls;
using PCL.UI.Controls.Behaviors;
using PCL.UI.Theming;

namespace PCL.Android;

/// <summary>Android 应用入口：主题、样式与页面全部复用桌面端，只替换窗口宿主。</summary>
public class PclAndroidApp : Avalonia.Application
{
    public override void Initialize()
    {
        // 必须在触碰任何 PCL.Core 类型前确定数据根目录（Paths 是静态构造）。
        AndroidPaths.Prepare();
        AndroidBootstrap.Breadcrumb("App.Initialize: 进入");

        // 必须先加载桌面程序集：App.axaml 里的 avares://PCL.Launcher/... 与
        // Basics 读取的 PCL.metadata.json 都依赖它已进入 AppDomain。
        ForceLoadDesktopAssembly();
        AndroidBootstrap.Breadcrumb("App.Initialize: 桌面程序集已加载");

        try
        {
            AvaloniaXamlLoader.Load(this);
            AndroidBootstrap.Breadcrumb("App.Initialize: 样式已加载");
            LaunchFonts.InstallGlobalFont();
            LocalizationService.LanguageChanged += LaunchFonts.InstallGlobalFont;

            // 桌面字体链里是 Windows/macOS 的字体名，Android 上没有 → 中文会渲染成空白。
            // 这里替换成 Android 自带的中文字体链（Roboto + Noto CJK）。
            if (Resources is { } resources)
                resources[LaunchFonts.ApplicationFontKey] =
                    new Avalonia.Media.FontFamily("Noto Sans CJK SC, Noto Sans SC, Roboto, sans-serif");

            AndroidBootstrap.Breadcrumb("App.Initialize: 字体已安装");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("App.Initialize", exception);
            Styles.Add(new FluentTheme());
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 页面依赖应用运行时（平台服务、配置、日志、本地化），必须先初始化再创建界面。
        AndroidRuntimeSetup.Initialize();
        AndroidBootstrap.Breadcrumb("App: 运行时已初始化");

        try
        {
            ThemeCoordinator.Install();
            Tooltip.Enable();
            ClipboardInterceptor.Enable();
            AndroidBootstrap.Breadcrumb("App: 主题与交互已安装");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("App.Theme", exception);
        }

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new DesktopShell();
            AndroidBootstrap.Breadcrumb("App: 界面已挂载");
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>强制加载 PCL.Launcher，使 avares 资源与内嵌元数据可被解析。</summary>
    private static void ForceLoadDesktopAssembly()
    {
        try
        {
            _ = typeof(PCL.Views.MainWindow).Assembly;
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("ForceLoadDesktop", exception);
        }

        try
        {
            Assembly.Load("PCL.Launcher");
        }
        catch (Exception)
        {
            // 已被 typeof 引用加载时这里会抛重复加载异常，忽略。
        }
    }
}

/// <summary>Android Application：把 AppBuilder 交给 Avalonia 的 Android 运行时。</summary>
[Application]
public class PclAndroidApplication : AvaloniaAndroidApplication<PclAndroidApp>
{
    protected PclAndroidApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // Avalonia 的 Application.OnCreate 会初始化整套启动器 UI。JVM 进程只运行服务。
        if (!AndroidJavaPaths.IsJvmProcess)
            base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .LogToTrace();
}
