using System;
using System.Collections.Generic;
using System.Linq;

namespace PCL.Core.Link.Astral;

/// <summary>房间成员：来自 easytier-cli 的 peer 表。</summary>
public sealed record AstralMember(string Name, string VirtualIp, string Latency, string Tunnel)
{
    /// <summary>
    ///     解析 <c>easytier-cli peer</c> 输出的 Markdown 表格。
    ///     列顺序与 EasyTier 一致：IP | hostname | ... | 延迟 | ... | 隧道。
    /// </summary>
    public static IReadOnlyList<AstralMember> ParseTable(string? table)
    {
        if (string.IsNullOrWhiteSpace(table)) return [];

        var members = new List<AstralMember>();
        foreach (var line in table.Split('\n'))
        {
            var text = line.Trim();
            if (!text.StartsWith('|')) continue;
            if (text.Contains("hostname", StringComparison.OrdinalIgnoreCase)) continue;
            if (text.Contains("---", StringComparison.Ordinal)) continue;

            var cells = text.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(cell => cell.Trim())
                .ToArray();
            if (cells.Length < 2 || cells[1].Length == 0) continue;

            members.Add(new AstralMember(
                cells[1],
                cells[0].Length == 0 ? "—" : cells[0],
                cells.Length > 3 ? cells[3] : "—",
                cells.Length > 7 ? cells[7] : "—"));
        }

        return members;
    }

    public string Summary => $"{VirtualIp} · 延迟 {Latency} · {Tunnel}";
}
