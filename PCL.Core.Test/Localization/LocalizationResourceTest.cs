using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCL.Core.Test;

/// <summary>
///     本地化资源护栏：键查重、多语言键对齐、XAML 引用可解析。
///     重复键会让整个 ResourceDictionary 加载失败（表现为满屏 !key!），这里在测试阶段拦住。
/// </summary>
[TestClass]
public class LocalizationResourceTest
{
    private static readonly string[] Languages = ["zh-CN", "zh-TW", "en-US", "en-GB"];

    private static readonly Regex KeyRegex = new("x:Key=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex LocRefRegex = new("\\{loc:Loc ([^}\\s]+)\\}", RegexOptions.Compiled);

    private static string? _repoRoot;

    [ClassInitialize]
    public static void Locate(TestContext context)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PCL.Localization", "Languages", "zh-CN.axaml")))
            {
                _repoRoot = directory.FullName;
                return;
            }
        }
    }

    private static string LanguagePath(string language)
    {
        Assert.IsNotNull(_repoRoot, "未能定位仓库根目录，无法检查本地化资源");
        return Path.Combine(_repoRoot, "PCL.Localization", "Languages", language + ".axaml");
    }

    private static List<string> ReadKeys(string path)
    {
        var text = File.ReadAllText(path);
        return KeyRegex.Matches(text).Select(match => match.Groups[1].Value).ToList();
    }

    [TestMethod]
    public void LanguageFilesHaveNoDuplicateKeys()
    {
        foreach (var language in Languages)
        {
            var keys = ReadKeys(LanguagePath(language));
            var duplicates = keys.GroupBy(key => key, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            Assert.AreEqual(0, duplicates.Count,
                language + " 存在重复键：" + string.Join(", ", duplicates.Take(5)));
        }
    }

    [TestMethod]
    public void AllLanguagesShareTheSameKeySet()
    {
        var reference = ReadKeys(LanguagePath("zh-CN")).ToHashSet(StringComparer.Ordinal);
        foreach (var language in Languages.Skip(1))
        {
            var keys = ReadKeys(LanguagePath(language)).ToHashSet(StringComparer.Ordinal);
            var missing = reference.Except(keys).Take(5).ToList();
            var extra = keys.Except(reference).Take(5).ToList();
            Assert.AreEqual(0, missing.Count, language + " 缺少键：" + string.Join(", ", missing));
            Assert.AreEqual(0, extra.Count, language + " 多出键：" + string.Join(", ", extra));
        }
    }

    [TestMethod]
    public void EveryXamlLocReferenceExists()
    {
        Assert.IsNotNull(_repoRoot, "未能定位仓库根目录");
        var known = ReadKeys(LanguagePath("zh-CN")).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(_repoRoot, "PCL.Launcher"), "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains("/obj/", StringComparison.Ordinal)) continue;
            foreach (Match match in LocRefRegex.Matches(File.ReadAllText(file)))
            {
                var key = match.Groups[1].Value;
                if (!known.Contains(key))
                    missing.Add(Path.GetFileName(file) + ": " + key);
            }
        }

        Assert.AreEqual(0, missing.Distinct().Count(),
            "XAML 引用了不存在的本地化键：" + string.Join(", ", missing.Distinct().Take(8)));
    }
}
