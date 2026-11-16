using System;
using System.Collections.Generic;
using System.Linq;

namespace PCL;

/// <summary>单个 Mod 的更新检查状态。</summary>
public enum ModUpdateState
{
    /// <summary>有更新的版本可装。</summary>
    UpdateAvailable,

    /// <summary>已是最新。</summary>
    UpToDate,

    /// <summary>查不到来源（本地自制、其它平台或哈希未收录）。</summary>
    Unknown,

    /// <summary>来源里有这个 Mod，但没有适配当前游戏版本/加载器的文件。</summary>
    NoCompatibleVersion,

    /// <summary>检查过程出错。</summary>
    Failed
}

/// <summary>一条检查结果。</summary>
public sealed record ModUpdateItem(
    string Name,
    string FilePath,
    ModUpdateState State,
    string? CurrentVersion = null,
    string? LatestVersion = null,
    string? Detail = null)
{
    /// <summary>是否可一键更新。</summary>
    public bool CanApply => State == ModUpdateState.UpdateAvailable;

    /// <summary>给人看的一行描述。</summary>
    public string Description => State switch
    {
        ModUpdateState.UpdateAvailable =>
            (CurrentVersion is null ? "有新版本" : CurrentVersion + " → " + LatestVersion),
        ModUpdateState.UpToDate => "已是最新",
        ModUpdateState.Unknown => Detail ?? "未识别来源",
        ModUpdateState.NoCompatibleVersion => Detail ?? "没有适配当前版本的更新",
        _ => Detail ?? "检查失败"
    };
}

/// <summary>一次检查的完整结果。</summary>
public sealed record ModUpdateReport(IReadOnlyList<ModUpdateItem> Items)
{
    public int CountOf(ModUpdateState state) => Items.Count(item => item.State == state);

    public IReadOnlyList<ModUpdateItem> Applicable =>
        Items.Where(item => item.CanApply).ToList();

    /// <summary>「可更新 3 项，已最新 12 项，未识别 2 项」这类汇总。</summary>
    public string Summary
    {
        get
        {
            if (Items.Count == 0) return "没有已启用的 Mod 可检查";
            if (Items.All(item => item.State == ModUpdateState.UpToDate)) return "所有已检查 Mod 均为最新";

            var parts = new List<string>();
            void Add(ModUpdateState state, string label)
            {
                var count = CountOf(state);
                if (count > 0) parts.Add(label + " " + count + " 项");
            }

            Add(ModUpdateState.UpdateAvailable, "可更新");
            Add(ModUpdateState.UpToDate, "已最新");
            Add(ModUpdateState.NoCompatibleVersion, "无兼容版本");
            Add(ModUpdateState.Unknown, "未识别");
            Add(ModUpdateState.Failed, "检查失败");
            return string.Join("，", parts);
        }
    }
}

/// <summary>
///     把本地 Mod 的既有更新标记（<see cref="LocalComp.LocalCompFile.CanUpdate" /> /
///     <see cref="LocalComp.LocalCompFile.UpdateFile" />）整理成可展示、可批量应用的计划。
///     网络查询仍由 <see cref="LocalComp" /> 的现有流程完成，这里只做状态判定。
/// </summary>
public static class ModUpdatePlanner
{
    /// <summary>按本地文件列表生成计划；只统计已启用的 Mod。</summary>
    public static ModUpdateReport Build(IEnumerable<LocalComp.LocalCompFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var items = new List<ModUpdateItem>();

        foreach (var file in files)
        {
            if (file.State != LocalComp.LocalCompFile.LocalFileStatus.Fine) continue;

            try
            {
                items.Add(Describe(file));
            }
            catch (Exception exception)
            {
                items.Add(new ModUpdateItem(
                    file.Name,
                    file.ActualPath,
                    ModUpdateState.Failed,
                    Detail: exception.Message));
            }
        }

        return new ModUpdateReport(items);
    }

    private static ModUpdateItem Describe(LocalComp.LocalCompFile file)
    {
        var name = file.Name;
        var path = file.ActualPath;
        var current = file.CompFile?.Version;

        if (file.CompFile is null || string.IsNullOrWhiteSpace(file.CompFile.Id))
            return new ModUpdateItem(name, path, ModUpdateState.Unknown,
                current, Detail: "不是从 Modrinth / CurseForge 下载的文件");

        var latest = file.UpdateFile;
        if (latest is null)
            return new ModUpdateItem(name, path, ModUpdateState.NoCompatibleVersion, current,
                Detail: "来源里没有适配当前游戏版本与加载器的文件");

        if (!file.CanUpdate || string.Equals(latest.Version, current, StringComparison.Ordinal))
            return new ModUpdateItem(name, path, ModUpdateState.UpToDate, current, latest.Version);

        return new ModUpdateItem(name, path, ModUpdateState.UpdateAvailable, current, latest.Version);
    }
}
