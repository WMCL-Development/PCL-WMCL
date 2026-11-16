using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace PCL;

/// <summary>一处缺失或损坏的文件。</summary>
public sealed record MissingFile(string Kind, string Path, string? Url, string? Sha1);

/// <summary>校验结果：检查了多少项、缺了哪些。</summary>
public sealed record IntegrityReport(int Checked, IReadOnlyList<MissingFile> Missing)
{
    public bool IsHealthy => Missing.Count == 0;

    public string Summary => Missing.Count == 0
        ? $"检查 {Checked} 个文件，全部正常"
        : $"检查 {Checked} 个文件，缺失 {Missing.Count} 个";
}

/// <summary>
///     版本完整性校验：按版本 JSON 里的清单逐个检查客户端 jar、依赖库与资源索引是否就位。
///     只做检查，不下载；修复动作交给下载管线，避免这里耦合网络。
/// </summary>
public static class VersionIntegrityChecker
{
    /// <summary>检查一个实例；不抛异常，读不到的清单项会被跳过。</summary>
    public static IntegrityReport Check(McInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var missing = new List<MissingFile>();
        var checkedCount = 0;

        var versionDirectory = instance.PathInstance.TrimEnd(Path.DirectorySeparatorChar);
        var librariesDirectory = Path.Combine(instance.PathIndie, "libraries");

        JsonObject json;
        try
        {
            json = instance.JsonObject;
        }
        catch (Exception)
        {
            return new IntegrityReport(0, [new MissingFile("json", Path.Combine(versionDirectory, instance.Name + ".json"), null, null)]);
        }

        // 客户端 jar
        var clientJar = Path.Combine(versionDirectory, instance.Name + ".jar");
        checkedCount++;
        if (!File.Exists(clientJar))
        {
            var clientUrl = json["downloads"]?["client"]?["url"]?.ToString();
            var clientSha1 = json["downloads"]?["client"]?["sha1"]?.ToString();
            missing.Add(new MissingFile("client", clientJar, clientUrl, clientSha1));
        }

        // 依赖库
        if (json["libraries"] is JsonArray libraries)
        {
            foreach (var library in libraries)
            {
                var artifact = library?["downloads"]?["artifact"];
                var relative = artifact?["path"]?.ToString();
                if (string.IsNullOrWhiteSpace(relative)) continue;

                checkedCount++;
                var full = Path.Combine(librariesDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                    missing.Add(new MissingFile("library", full, artifact?["url"]?.ToString(),
                        artifact?["sha1"]?.ToString()));
            }
        }

        // 资源索引（不逐个校验 objects，避免上万个文件的 IO）
        var assetIndex = json["assetIndex"];
        if (assetIndex?["id"]?.ToString() is { Length: > 0 } assetId)
        {
            checkedCount++;
            var indexPath = Path.Combine(instance.PathIndie, "assets", "indexes", assetId + ".json");
            if (!File.Exists(indexPath))
                missing.Add(new MissingFile("asset-index", indexPath,
                    assetIndex["url"]?.ToString(), assetIndex["sha1"]?.ToString()));
        }

        return new IntegrityReport(checkedCount, missing);
    }
}
