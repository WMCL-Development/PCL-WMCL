using Microsoft.Extensions.DependencyInjection;
using PCL.Core.Platform;
using PCL.Core.UI;
using PCL.Platform;

namespace PCL.Platform.Android;

/// <summary>Android 平台的 DI 注册：把桌面端依赖的平台/UI 服务替换为 Android 实现。</summary>
public static class AndroidServiceRegistration
{
    /// <summary>注册 Android 平台服务；无法实现的能力使用显式空实现。</summary>
    public static IServiceCollection AddAndroidPlatformServices(this IServiceCollection services)
    {
        services.AddSingleton<IExternalCommandRunner, ExternalCommandRunner>();
        services.AddSingleton<IElevationService, PlatformElevationService>();
        services.AddSingleton<IProcessLauncher, PlatformProcessLauncher>();
        services.AddSingleton<ISystemProxyProvider, AndroidSystemProxyProvider>();
        services.AddSingleton<IWindowInterop, AndroidWindowInterop>();
        services.AddSingleton<IShortcutService, AndroidShortcutService>();
        services.AddSingleton<IJavaPlatformInfo, AndroidJavaPlatformInfo>();
        services.AddSingleton<IProcessCleanupService, AndroidProcessCleanupService>();
        services.AddSingleton<IGpuPreferenceService, UnsupportedGpuPreferenceService>();
        services.AddSingleton<ILaunchEnvironmentService, UnsupportedLaunchEnvironmentService>();
        services.AddSingleton<ISystemFacts, AndroidSystemFacts>();
        services.AddSingleton<IRuntimeEnvironmentService, LinuxRuntimeEnvironmentService>();
        services.AddSingleton<IHardwareInfo, AndroidHardwareInfo>();
        services.AddSingleton<IShellLauncher, AndroidShellLauncher>();
        services.AddSingleton<IRegionSignalProvider, PosixRegionSignalProvider>();
        services.AddSingleton<IRecycleBinService, AndroidRecycleBinService>();
        services.AddSingleton<IFatalDialogService, LinuxFatalDialogService>();
        services.AddSingleton<IKeyProtectorFactory, UnixKeyProtectorFactory>();
        services.AddSingleton<IPlatformPathService, LinuxPlatformPathService>();
        services.AddSingleton<ILaunchScriptService, PosixLaunchScriptService>();
        services.AddSingleton<IServerLaunchScriptService>(_ => new PosixServerLaunchScriptService(".sh"));
        services.AddSingleton<IMinecraftPlatformRules, AndroidMinecraftPlatformRules>();
        services.AddSingleton<IShellCommandService, PosixShellCommandService>();
        services.AddSingleton<IProcessEnvironmentService, PosixProcessEnvironmentService>();
        return services;
    }

    /// <summary>注册 Android 的 Avalonia UI 服务。</summary>
    public static IServiceCollection AddAndroidUiServices(this IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IClipboardService, AndroidClipboardService>();
        services.AddSingleton<IFileDialogService, AndroidFileDialogService>();
        services.AddSingleton<IResourceStore, ResourceStore>();
        services.AddSingleton<ILocalizationBackend>(provider =>
            new LocalizationBackend(provider.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<IThemeBackend, ThemeBackend>();
        services.AddSingleton<ISystemThemeProvider, AndroidSystemThemeProvider>();
        services.AddSingleton<ISystemNotificationService, AndroidNotificationService>();
        return services;
    }
}
