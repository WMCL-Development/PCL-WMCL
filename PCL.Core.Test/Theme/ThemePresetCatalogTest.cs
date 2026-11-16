using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App;
using PCL.Core.UI.Theme;

namespace PCL.Core.Test;

[TestClass]
public class ThemePresetCatalogTest
{
    [TestMethod]
    public void EveryPresetHasLocalizedNameAndUniqueValue()
    {
        var all = ThemePresetCatalog.All;
        Assert.IsTrue(all.Count >= 7, "配色预设应至少有 7 套");
        Assert.AreEqual(all.Count, all.Select(preset => preset.Value).Distinct().Count(),
            "配色枚举值不能重复");
        Assert.AreEqual(all.Count, all.Select(preset => preset.NameKey).Distinct().Count(),
            "配色本地化键不能重复");
    }

    [TestMethod]
    public void VisibleHidesAprilFoolOnlyPresets()
    {
        var normal = ThemePresetCatalog.Visible(isAprilFool: false);
        var aprilFool = ThemePresetCatalog.Visible(isAprilFool: true);

        Assert.IsFalse(normal.Any(preset => preset.AprilFoolOnly));
        Assert.IsTrue(aprilFool.Count > normal.Count);
        Assert.IsTrue(aprilFool.Any(preset => preset.AprilFoolOnly));
    }

    [TestMethod]
    public void ArgsOfFallsBackForUnknownValue()
    {
        var args = ThemePresetCatalog.ArgsOf((ColorTheme)99);
        Assert.AreEqual(255, args.Hue);
        Assert.AreEqual(0d, args.LightAdjust);
        Assert.AreEqual(-0.2d, args.ChromaAdjust);
    }

    [TestMethod]
    public void ArgsOfReturnsPresetValues()
    {
        var preset = ThemePresetCatalog.All.First();
        var args = ThemePresetCatalog.ArgsOf(preset.Value);
        Assert.AreEqual(preset.Hue, args.Hue);
        Assert.AreEqual(preset.LightAdjust, args.LightAdjust);
        Assert.AreEqual(preset.ChromaAdjust, args.ChromaAdjust);
    }
}
