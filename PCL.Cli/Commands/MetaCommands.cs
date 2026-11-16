using System;
using System.IO;
using System.Linq;
using PCL.Core.App;

namespace PCL.Cli.Commands;

/// <summary>环境与元信息命令：版本、数据目录、配置路径。</summary>
internal static class MetaCommands
{
    public static int Version()
    {
        var core = typeof(Paths).Assembly.GetName().Version;
        CliConsole.Line("pcl-cli " + (core?.ToString() ?? "unknown"));
        CliConsole.Line("runtime " + Environment.Version);
        CliConsole.Line("os " + Environment.OSVersion.VersionString);
        CliConsole.Line("data " + Paths.Data);
        return 0;
    }

    public static int Paths_(CliOptions options)
    {
        CliConsole.Line("数据目录   " + Paths.Data);
        CliConsole.Line("配置目录   " + Paths.Data);
        CliConsole.Line("临时目录   " + Paths.Temp);
        var game = options.Get("dir");
        if (!string.IsNullOrWhiteSpace(game))
            CliConsole.Line("游戏目录   " + Path.GetFullPath(game));
        return 0;
    }
}
