using System;
using System.IO;
using PCL;
using PCL.Core.App;
using PCL.Core.Logging;
using PCL.Core.Platform;

namespace PCL.Cli;

/// <summary>
///     无界面运行时的初始化：数据目录、加载器通知与提示通道。
///     与界面版的启动流程职责对应，但不依赖任何 UI 框架。
/// </summary>
internal static class CliHost
{
    public static bool Initialized { get; private set; }

    /// <summary>按参数初始化；dataDirectory 为空时用默认目录。</summary>
    public static void Initialize(string? dataDirectory, bool quiet)
    {
        if (Initialized) return;
        Initialized = true;
        CliConsole.Quiet = quiet;

        // 1. 注册阶段：任何 Config 分组被触碰前必须完成配置项登记。
        PCL.Core.App.Configuration.ConfigService.BeginRegistration();

        // 2. 数据目录（会触碰 Paths / Basics，此时登记已完成，安全）。
        if (!string.IsNullOrWhiteSpace(dataDirectory))
            Paths.Data = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(Paths.Data);

        // 3. 完整初始化配置（读文件 + 观察者），下载等能力需要读配置值。
        try
        {
            PCL.Core.App.Configuration.ConfigService.Initialize();
        }
        catch (Exception exception)
        {
            CliConsole.Error("[配置初始化失败] " + exception);
        }

        // 4. 网络服务（下载需要 HttpClient 工厂）。
        try
        {
            PCL.Core.IO.Net.NetworkService.Initialize();
        }
        catch (Exception exception)
        {
            CliConsole.Error("[网络初始化失败] " + exception.Message);
        }

        // 5. 装配平台与 UI 服务（不创建窗口，仅提供无头运行所需实现）。
        if (!ApplicationRuntime.IsInitialized)
        {
            var kind = PlatformDetector.Detect();
            ApplicationRuntime.Initialize(new ApplicationServices
            {
                Platform = PCL.Platform.PlatformModuleFactory.Create(kind),
                Ui = PCL.Core.UI.UiModuleFactory.Create(kind)
            });
        }

        // 5. 加载器进度与提示走终端实现。
        LauncherCommon.HeadlessMode = true;
        LaunchUi.Current = new CliLaunchUi();
        LoadingUi.Current = new CliLoadingUi();
        LauncherCommon.UiLogHandler = (_, text, level, _, userSummary) =>
        {
            var message = userSummary ?? text;
            if (level >= LauncherLogLevel.Feedback) CliConsole.Error(message);
            else CliConsole.Line(message);
        };

        LogWrapper.Info("Cli", "命令行模式已启动，数据目录：" + Paths.Data);
    }
}
