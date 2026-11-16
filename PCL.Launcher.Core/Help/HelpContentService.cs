using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PCL.Core.App;
using PCL.Core.Logging;

namespace PCL;

/// <summary>
///     内置帮助：把随程序打包的帮助文件解压到数据目录后按普通文件打开。
///     与 PCL2 的帮助机制保持一致——帮助页只认磁盘上的 JSON + XAML 组合。
/// </summary>
public static class HelpContentService
{
    private const string ResourcePrefix = "PCL.Resources.Help.";

    /// <summary>全部内置帮助主题的键名。</summary>
    public static readonly IReadOnlyList<string> Topics =
    [
        "features", "launch", "link", "tools", "setup", "cli"
    ];

    /// <summary>解压全部内置帮助并返回「功能总览」的 JSON 路径；失败时返回 null。</summary>
    public static string? EnsureFeaturesHelp() => EnsureTopic("features");

    /// <summary>解压指定帮助主题并返回 JSON 路径；主题不存在时返回 null。</summary>
    public static string? EnsureTopic(string topic)
    {
        if (!Topics.Contains(topic)) return null;
        try
        {
            var directory = Path.Combine(Paths.Data, "Help");
            Directory.CreateDirectory(directory);
            var jsonPath = Path.Combine(directory, topic + ".json");
            var xamlPath = Path.Combine(directory, topic + ".xaml");

            Extract(topic + ".json", jsonPath);
            Extract(topic + ".xaml", xamlPath);
            return jsonPath;
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Help", "内置帮助解压失败：" + exception.Message);
            return null;
        }
    }

    private static void Extract(string name, string targetPath)
    {
        // 帮助资源打包在界面程序集里，本类位于核心程序集，需要遍历已加载程序集查找。
        var resourceName = ResourcePrefix + name;
        var stream = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetManifestResourceStream(resourceName))
            .FirstOrDefault(candidate => candidate is not null);
        if (stream is null)
            throw new FileNotFoundException("未找到内置帮助资源：" + resourceName);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        File.WriteAllText(targetPath, reader.ReadToEnd());
    }
}
