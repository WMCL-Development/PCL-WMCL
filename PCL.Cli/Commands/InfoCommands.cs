using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace PCL.Cli.Commands;

/// <summary>实例信息命令：直接读版本 JSON，不依赖界面与实例列表加载器。</summary>
internal static class InfoCommands
{
    public static int Run(CliOptions options)
    {
        var gameDirectory = options.Get("dir") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft");
        var versions = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versions))
        {
            CliConsole.Error("未找到版本目录：" + versions);
            return 1;
        }

        var name = options.Positional.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            CliConsole.Error("用法：pcl-cli info <实例名> [--dir <游戏目录>]");
            return 2;
        }

        var directory = Path.Combine(versions, name);
        var jsonPath = Path.Combine(directory, name + ".json");
        if (!File.Exists(jsonPath))
        {
            CliConsole.Error("未找到实例：" + directory);
            return 1;
        }

        JsonObject json;
        try
        {
            json = (JsonObject)JsonNode.Parse(File.ReadAllText(jsonPath))!;
        }
        catch (Exception exception)
        {
            CliConsole.Error("版本 JSON 解析失败：" + exception.Message);
            return 1;
        }

        var libraries = (json["libraries"] as JsonArray)?.Count ?? 0;
        var modsDirectory = Path.Combine(directory, "mods");
        var mods = Directory.Exists(modsDirectory)
            ? Directory.GetFiles(modsDirectory, "*.jar").Length
            : 0;

        CliConsole.Line("实例       " + name);
        CliConsole.Line("版本 id    " + (json["id"]?.ToString() ?? name));
        CliConsole.Line("类型       " + (json["type"]?.ToString() ?? "unknown"));
        CliConsole.Line("主类       " + (json["mainClass"]?.ToString() ?? "unknown"));
        CliConsole.Line("依赖库     " + libraries + " 个");
        CliConsole.Line("Mod        " + mods + " 个");
        CliConsole.Line("资源索引   " + (json["assetIndex"]?["id"]?.ToString() ?? "无"));
        CliConsole.Line("目录       " + directory);
        return 0;
    }
}
