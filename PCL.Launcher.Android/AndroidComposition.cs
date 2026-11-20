using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using PCL;
using PCL.Core.Platform;
using PCL.Core.UI;
using PCL.Navigation;
using PCL.Platform;
using PCL.Platform.Android;

namespace PCL.Android;

/// <summary>Android 组合根：初始化应用运行时、构建 DI 容器并接入页面宿主工厂。</summary>
public static class AndroidComposition
{
    private static ServiceProvider? _services;

    /// <summary>当前服务容器。</summary>
    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Android 组合根尚未初始化");

    public static void Initialize()
    {
        PlatformServiceRegistration.CustomRegistration = services => services.AddAndroidPlatformServices();
        AvaloniaUiServiceRegistration.CustomRegistration = services =>
        {
            services.AddAndroidUiServices();
            services.AddSingleton<IJavaSetupPageFactory, AndroidJavaSetupPageFactory>();
        };

        var applicationServices = new ApplicationServices
        {
            Platform = new PlatformModule
            {
                Kind = PlatformKind.Android,
                Capabilities = PlatformCapabilities.Android
            },
            Ui = new UiModule()
        };
        ApplicationRuntime.Initialize(applicationServices);

        _services = LauncherServiceRegistration.Build(applicationServices);
        PageRegistry.Initialize(_services.GetRequiredService<PageHostFactory>());
    }

    /// <summary>
    /// Points the launcher's existing download and launch pipeline at app-private storage and
    /// at the natives that ship inside the APK. Without this the mature Minecraft code would
    /// keep resolving desktop paths that do not exist on Android.
    /// </summary>
    public static void ConfigureMinecraftStorage(Context context)
    {
        var root = Path.Combine(context.FilesDir!.AbsolutePath, "minecraft");
        global::Android.Util.Log.Info("WMCL.MC", "Minecraft 存储：" + root);
        Directory.CreateDirectory(root);
        GameFolders.ApplySelectedFolder(root);
        // Android keeps every native library in the app's lib directory; there is nothing to
        // extract from the version jars.
        GameLaunch.PlatformNativesFolder = () => context.ApplicationInfo!.NativeLibraryDir!;
    }
}
