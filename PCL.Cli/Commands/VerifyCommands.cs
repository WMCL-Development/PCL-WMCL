using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PCL;

namespace PCL.Cli.Commands;

/// <summary>版本完整性校验命令：不启动游戏，只按清单核对文件是否齐全。</summary>
internal static class VerifyCommands
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

        var targets = options.Positional.Skip(1).ToList();
        var instances = Directory.GetDirectories(versions)
            .Where(directory => targets.Count == 0
                || targets.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (instances.Count == 0)
        {
            CliConsole.Error("没有匹配的实例");
            return 1;
        }

        var failed = false;
        foreach (var directory in instances)
        {
            var name = Path.GetFileName(directory);
            var jsonPath = Path.Combine(directory, name + ".json");
            if (!File.Exists(jsonPath))
            {
                CliConsole.Error(name + "：缺少版本 JSON");
                failed = true;
                continue;
            }

            var report = Check(directory, name, gameDirectory);
            CliConsole.Line(name + "：" + report.Summary);
            foreach (var item in report.Missing.Take(10))
                CliConsole.Line("  [" + item.Kind + "] " + item.Path);
            if (report.Missing.Count > 10)
                CliConsole.Line("  … 另有 " + (report.Missing.Count - 10) + " 项");
            if (!report.IsHealthy) failed = true;

            if (!report.IsHealthy && options.Has("fix"))
            {
                CliConsole.Line("开始补齐 " + report.Missing.Count + " 个文件…");
                var progress = new Progress<(int Done, int Total, string Path)>(item =>
                {
                    if (item.Total > 0) CliConsole.Progress("补齐", item.Done * 100d / item.Total);
                });
                var results = VersionRepairer.RepairAsync(report, progress).GetAwaiter().GetResult();
                CliConsole.ProgressDone();
                var ok = results.Count(result => result.Success);
                CliConsole.Line("补齐完成：成功 " + ok + " / 失败 " + (results.Count - ok));
                foreach (var result in results.Where(result => !result.Success))
                    CliConsole.Error("  失败 " + result.Path + "：" + result.Error);
                failed = ok != results.Count;
            }
        }

        return failed ? 1 : 0;
    }

    private static IntegrityReport Check(string versionDirectory, string name, string gameDirectory)
    {
        JsonObject json;
        try
        {
            json = (JsonObject)JsonNode.Parse(File.ReadAllText(
                Path.Combine(versionDirectory, name + ".json")))!;
        }
        catch (Exception exception)
        {
            CliConsole.Error(name + "：版本 JSON 解析失败（" + exception.Message + "）");
            return new IntegrityReport(0, []);
        }

        var missing = new System.Collections.Generic.List<MissingFile>();
        var checkedCount = 0;

        var clientJar = Path.Combine(versionDirectory, name + ".jar");
        checkedCount++;
        if (!File.Exists(clientJar))
            missing.Add(new MissingFile("client", clientJar,
                json["downloads"]?["client"]?["url"]?.ToString(),
                json["downloads"]?["client"]?["sha1"]?.ToString()));

        if (json["libraries"] is JsonArray libraries)
        {
            var librariesDirectory = Path.Combine(gameDirectory, "libraries");
            foreach (var library in libraries)
            {
                var artifact = library?["downloads"]?["artifact"];
                var relative = artifact?["path"]?.ToString();
                if (string.IsNullOrWhiteSpace(relative)) continue;
                checkedCount++;
                var full = Path.Combine(librariesDirectory,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                    missing.Add(new MissingFile("library", full,
                        artifact?["url"]?.ToString(), artifact?["sha1"]?.ToString()));
            }
        }

        if (json["assetIndex"]?["id"]?.ToString() is { Length: > 0 } assetId)
        {
            checkedCount++;
            var indexPath = Path.Combine(gameDirectory, "assets", "indexes", assetId + ".json");
            if (!File.Exists(indexPath))
                missing.Add(new MissingFile("asset-index", indexPath,
                    json["assetIndex"]?["url"]?.ToString(),
                    json["assetIndex"]?["sha1"]?.ToString()));
        }

        return new IntegrityReport(checkedCount, missing);
    }
}
