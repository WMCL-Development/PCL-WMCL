using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCL.Core.Tools.Lottery.Items;

namespace PCL.Cli.Commands;

/// <summary>物品解析命令：从原版 jar 与 Mod jar 中提取物品目录。</summary>
internal static class ItemCommands
{
    public static int Run(CliOptions options)
    {
        var gameDirectory = options.Get("dir");
        var modJars = new List<string>();
        // --mod 的值由参数解析器收走，位置参数里的 jar 也接受
        if (options.Get("mod") is { Length: > 0 } singleMod) modJars.Add(singleMod);
        modJars.AddRange(options.Positional.Where(arg =>
            arg.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)));

        if (modJars.Count == 0 && string.IsNullOrWhiteSpace(gameDirectory))
        {
            CliConsole.Error("用法：pcl-cli items [--dir <游戏目录>] [--mod <jar>]…");
            return 2;
        }

        var missing = modJars.Where(jar => !File.Exists(jar)).ToList();
        foreach (var jar in missing) CliConsole.Error("找不到文件：" + jar);
        if (missing.Count > 0) return 1;

        IReadOnlyList<MinecraftItem> items;
        try
        {
            items = MinecraftItemCatalog.Build(gameDirectory, null, modJars);
        }
        catch (Exception exception)
        {
            CliConsole.Error("解析失败：" + exception.Message);
            return 1;
        }

        if (items.Count == 0)
        {
            CliConsole.Error("没有解析到任何物品");
            return 1;
        }

        var modItems = items.Count(item => item.IsMod);
        foreach (var item in items.Take(50))
            CliConsole.Line((item.IsMod ? "[Mod] " : "      ") + item.Id + "\t" + item.Name);

        if (items.Count > 50) CliConsole.Line("… 另有 " + (items.Count - 50) + " 项");
        CliConsole.Line("共 " + items.Count + " 个物品（其中 Mod " + modItems + " 个）");
        return 0;
    }
}
