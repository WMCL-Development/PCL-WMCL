using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PCL.Cli.Commands;

namespace PCL.Cli;

/// <summary>
///     PCL CE 命令行入口：不启动任何界面，直接调用核心能力。
///     退出码：0 成功 / 1 命令失败 / 2 参数错误。
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return args.Length == 0 ? 2 : 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
            CliConsole.Error("已取消");
        };

        // 允许全局选项写在命令前：pcl-cli --data-dir /x speedtest
        // 只有这些全局选项带值，其余（如 --quiet）按开关处理
        string[] valueOptions = ["--data-dir"];
        var commandIndex = 0;
        while (commandIndex < args.Length && args[commandIndex].StartsWith("--", StringComparison.Ordinal))
            commandIndex += Array.IndexOf(valueOptions, args[commandIndex]) >= 0 ? 2 : 1;
        if (commandIndex >= args.Length)
        {
            PrintHelp();
            return 2;
        }

        var command = args[commandIndex].ToLowerInvariant();
        var globalOptions = commandIndex > 0 ? args[..commandIndex] : [];
        var rest = args[(commandIndex + 1)..];
        var options = CliOptions.Parse([.. globalOptions, .. rest]);
        CliHost.Initialize(options.Get("data-dir"), options.Has("quiet"));

        try
        {
            return command switch
            {
                "version" => MetaCommands.Version(),
                "paths" => MetaCommands.Paths_(options),
                "instances" => InstanceCommands.List(options),
                "java" => await JavaCommands.ListAsync(options).ConfigureAwait(false),
                "config" => ConfigCommands.Run(options),
                "verify" => VerifyCommands.Run(options),
                "info" => InfoCommands.Run(options),
                "launch" => LaunchCommands.Run(options),
                "login" => AccountCommands.Login(options),
                "account" => AccountCommands.Show(options),
                "lobby" => await LobbyCommands.RunAsync(options).ConfigureAwait(false),
                "easytier" => EasyTierCommands.Run(options),
                "items" => ItemCommands.Run(options),
                "speedtest" => await LinkCommands.SpeedTestAsync(options).ConfigureAwait(false),
                "astral" => LinkCommands.Astral(options),
                _ => Unknown(command)
            };
        }
        catch (OperationCanceledException)
        {
            CliConsole.Error("操作已取消");
            return 1;
        }
        catch (Exception exception)
        {
            CliConsole.Error("命令失败：" + exception.Message);
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        CliConsole.Error("未知命令：" + command);
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        CliConsole.Line("""
            pcl-cli — PCL CE 命令行工具（无需图形界面）

            用法：pcl-cli <命令> [选项]

            命令：
              version                       显示版本、运行时与数据目录
              paths [--dir <游戏目录>]      显示各目录路径
              instances [--dir <游戏目录>]  列出实例
              java [--json] [--path <java>] 扫描本机可用的 Java
              config list|get <键>|set <键> <值>  读写本地配置
              verify [实例…] [--dir <游戏目录>]   校验实例文件完整性
              info <实例> [--dir <游戏目录>]      显示实例信息
              launch <实例> [--wait] [--dir <目录>]  启动游戏（--wait 等待退出）
              login                          Microsoft 设备码登录
              account                        显示当前账号
              lobby host|join <编号>|parse <编号>|leave|status  联机大厅
              easytier info|join --network X --secret Y   EasyTier 直连
              items [--dir 目录] [--mod jar]…   解析物品目录（含 Mod）
              speedtest [--json]            测试下载源速度
              astral decode <邀请码>        解析长链邀请
              astral resolve <短码>         向服务换取短码对应房间
              astral rooms                  列出保存过的房间
              astral short <长链>           生成短链
              astral join <邀请码> [--wait] 加入房间（--wait 保持连接）
              astral host [--name 名称] [--port 端口] [--wait]  开房并输出邀请
              astral encode --name <名称>   生成 Astral 邀请链接

            全局选项：
              --data-dir <目录>             指定数据目录（默认与界面版相同）
              --quiet                       只输出错误

            退出码：0 成功 / 1 失败 / 2 参数错误
            """);
    }
}
