using System;
using System.IO;
using System.Linq;

namespace PCL.Cli.Commands;

/// <summary>实例相关命令：列出实例、查看实例目录。</summary>
internal static class InstanceCommands
{
    public static int List(CliOptions options)
    {
        var directory = options.Get("dir") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft");
        var versions = Path.Combine(directory, "versions");
        if (!Directory.Exists(versions))
        {
            CliConsole.Error("未找到版本目录：" + versions);
            return 1;
        }

        var names = Directory.GetDirectories(versions)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            CliConsole.Line("（没有实例）");
            return 0;
        }

        foreach (var name in names) CliConsole.Line(name);
        CliConsole.Line("共 " + names.Count + " 个实例");
        return 0;
    }
}
