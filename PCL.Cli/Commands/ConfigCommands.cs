using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCL.Core.App.Configuration;

namespace PCL.Cli.Commands;

/// <summary>
///     配置命令：直接读写本地配置文件（config.v1.yml）的标量键。
///     不启动生命周期，因此可在无界面环境下使用。
/// </summary>
internal static class ConfigCommands
{
    public static int Run(CliOptions options)
    {
        var path = ConfigService.LocalConfigPath;
        if (!File.Exists(path))
        {
            CliConsole.Error("配置文件不存在：" + path);
            return 1;
        }

        var action = options.Positional.FirstOrDefault() ?? "list";
        return action switch
        {
            "get" => Get(path, options),
            "set" => Set(path, options),
            "list" => List(path),
            _ => Unknown(action)
        };
    }

    private static int Get(string path, CliOptions options)
    {
        var key = options.Positional.Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            CliConsole.Error("用法：pcl-cli config get <键>");
            return 2;
        }

        var entry = ReadAll(path).FirstOrDefault(pair =>
            string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
        if (entry.Key is null)
        {
            CliConsole.Error("未找到配置项：" + key);
            return 1;
        }

        CliConsole.Line(entry.Key + ": " + entry.Value);
        return 0;
    }

    private static int Set(string path, CliOptions options)
    {
        var positional = options.Positional;
        if (positional.Count < 3)
        {
            CliConsole.Error("用法：pcl-cli config set <键> <值>");
            return 2;
        }

        var key = positional[1];
        var value = string.Join(' ', positional.Skip(2));
        var lines = File.ReadAllLines(path).ToList();
        var replaced = false;

        for (var index = 0; index < lines.Count; index++)
        {
            var colon = lines[index].IndexOf(':');
            if (colon <= 0) continue;
            if (!string.Equals(lines[index][..colon].Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;
            lines[index] = key + ": " + value;
            replaced = true;
            break;
        }

        if (!replaced) lines.Add(key + ": " + value);
        File.WriteAllLines(path, lines);
        CliConsole.Line((replaced ? "已更新 " : "已新增 ") + key + " = " + value);
        return 0;
    }

    private static int List(string path)
    {
        var entries = ReadAll(path);
        foreach (var (key, value) in entries) CliConsole.Line(key + ": " + value);
        CliConsole.Line("共 " + entries.Count + " 项（" + path + "）");
        return 0;
    }

    private static List<(string Key, string Value)> ReadAll(string path)
    {
        var result = new List<(string, string)>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            result.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }

        return result;
    }

    private static int Unknown(string action)
    {
        CliConsole.Error("未知子命令：" + action + "（可用：list / get / set）");
        return 2;
    }
}
