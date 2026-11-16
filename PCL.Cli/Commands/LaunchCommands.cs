using System;
using System.IO;
using System.Linq;
using System.Threading;
using PCL;

namespace PCL.Cli.Commands;

/// <summary>启动命令：按实例名启动游戏，走与界面完全相同的启动管线。</summary>
internal static class LaunchCommands
{
    public static int Run(CliOptions options)
    {
        var name = options.Positional.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            CliConsole.Error("用法：pcl-cli launch <实例名> [--dir <游戏目录>] [--wait]");
            return 2;
        }

        var gameDirectory = options.Get("dir");
        if (!string.IsNullOrWhiteSpace(gameDirectory))
            LauncherPaths.MinecraftFolder = Path.GetFullPath(gameDirectory);

        McInstance instance;
        try
        {
            instance = new McInstance(name);
            instance.Load();
        }
        catch (Exception exception)
        {
            CliConsole.Error("加载实例失败：" + exception.Message);
            return 1;
        }

        if (instance.state == McInstanceState.Error)
        {
            CliConsole.Error("实例不可启动：" + instance.Desc);
            return 1;
        }

        CliConsole.Line("启动实例：" + instance.Name);
        var started = GameLaunch.McLaunchStart(new GameLaunch.McLaunchOptions
        {
            instance = instance
        });
        if (!started)
        {
            CliConsole.Error("启动请求被拒绝（详见上方日志）");
            return 1;
        }

        // 等待启动管线结束（默认 3 分钟），期间由 CliLaunchUi 输出进度。
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            var state = GameLaunch.mcLaunchLoader.State;
            if (state is LoadState.Finished or LoadState.Failed or LoadState.Aborted) break;
            Thread.Sleep(200);
        }

        var finalState = GameLaunch.mcLaunchLoader.State;
        if (finalState != LoadState.Finished)
        {
            CliConsole.Error("启动未完成，状态：" + finalState);
            return 1;
        }

        var process = GameLaunch.mcLaunchProcess;
        if (process is null)
        {
            CliConsole.Line("已提交启动，但未获取到进程句柄");
            return 0;
        }

        CliConsole.Line("游戏进程 PID：" + process.Id);
        if (!options.Has("wait")) return 0;

        CliConsole.Line("等待游戏退出…");
        process.WaitForExit();
        CliConsole.Line("游戏已退出，退出码：" + process.ExitCode);
        return process.ExitCode;
    }
}
