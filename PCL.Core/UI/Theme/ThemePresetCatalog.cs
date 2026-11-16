using System;
using System.Collections.Generic;
using System.Linq;
using PCL.Core.App;

namespace PCL.Core.UI.Theme;

/// <summary>一套配色预设：枚举值、本地化键与生成色板用的 LCH 参数。</summary>
public sealed record ThemePresetInfo(
    ColorTheme Value,
    string NameKey,
    int Hue,
    double LightAdjust,
    double ChromaAdjust,
    bool AprilFoolOnly = false);

/// <summary>
///     配色预设表。新增配色只需要在这里加一行 + 补一条本地化文案，
///     设置页的下拉列表与色板生成都会自动跟上。
/// </summary>
public static class ThemePresetCatalog
{
    /// <summary>全部预设，顺序即设置页下拉顺序。</summary>
    public static IReadOnlyList<ThemePresetInfo> All { get; } =
    [
        new(ColorTheme.SkyBlue, "Setup.Ui.Theme.Color.SkyBlue", 235, 0.36, 0.2),
        new(ColorTheme.CatBlue, "Setup.Ui.Theme.Color.CatBlue", 255, 0, -0.2),
        new(ColorTheme.DeathBlue, "Setup.Ui.Theme.Color.CrashBlue", 268, -0.05, -0.1),
        new(ColorTheme.Mint, "Setup.Ui.Theme.Color.Mint", 165, 0.05, 0.1),
        new(ColorTheme.Amber, "Setup.Ui.Theme.Color.Amber", 65, 0.1, 0.15),
        new(ColorTheme.Rose, "Setup.Ui.Theme.Color.Rose", 350, 0.02, 0.12),
        new(ColorTheme.Violet, "Setup.Ui.Theme.Color.Violet", 300, -0.02, 0),
        new(ColorTheme.HmclBlue, "Setup.Ui.Theme.Color.Hmcl", 275, -0.03, -0.35, AprilFoolOnly: true)
    ];

    /// <summary>当前可见的预设（愚人节彩蛋项平时隐藏）。</summary>
    public static IReadOnlyList<ThemePresetInfo> Visible(bool isAprilFool) =>
        All.Where(preset => isAprilFool || !preset.AprilFoolOnly).ToList();

    /// <summary>按枚举值查预设；找不到返回 null。</summary>
    public static ThemePresetInfo? Find(ColorTheme value) =>
        All.FirstOrDefault(preset => preset.Value == value);

    /// <summary>色板生成用的三元组；未知值退回默认蓝，避免崩溃。</summary>
    public static (int Hue, double LightAdjust, double ChromaAdjust) ArgsOf(ColorTheme value)
    {
        var preset = Find(value);
        return preset is null
            ? (255, 0, -0.2)
            : (preset.Hue, preset.LightAdjust, preset.ChromaAdjust);
    }
}
