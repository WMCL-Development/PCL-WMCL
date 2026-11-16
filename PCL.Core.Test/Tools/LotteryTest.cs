using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Tools.Lottery;
using PCL.Core.Tools.Lottery.Items;

namespace PCL.Core.Test;

[TestClass]
public class LotteryTest
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcl-lottery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结果
        }
    }

    [DataTestMethod]
    [DataRow("diamond", "稀有")]
    [DataRow("emerald_block", "稀有")]
    [DataRow("minecraft:netherite_ingot", "史诗")]
    [DataRow("nether_star", "传说")]
    [DataRow("dragon_egg", "传说")]
    [DataRow("iron_ingot", "少见")]
    [DataRow("cobblestone", "常见")]
    [DataRow("zombie_spawn_egg", "常见")]
    [DataRow("mod_a:copper_gear", "少见")]
    public void ClassifyRarityFollowsCatalog(string id, string expected) =>
        Assert.AreEqual(expected, LotteryPrizeCatalog.ClassifyRarity(id));

    [TestMethod]
    public void FallbackPoolIsBalancedAndNonEmpty()
    {
        var pool = LotteryPrizeCatalog.BuildFallback();
        Assert.IsTrue(pool.Count >= 10);
        Assert.IsTrue(pool.All(prize => prize.Weight > 0));
        Assert.IsTrue(pool.All(prize => prize.RateText.Contains('%')));
        Assert.AreEqual(100d, pool.Sum(prize => prize.Weight) * 100d / pool.Sum(prize => prize.Weight), 0.001);
    }

    [TestMethod]
    public void BuildFromItemsGivesEachItemItsRarityWeight()
    {
        var items = new List<MinecraftItem>
        {
            new("diamond", "钻石", "/tmp/a.png", false),
            new("emerald", "绿宝石", "/tmp/b.png", false),
            new("cobblestone", "圆石", "/tmp/c.png", false)
        };
        var pool = LotteryPrizeCatalog.BuildFromItems(items);
        Assert.AreEqual(3, pool.Count);

        // 新规则：每件物品固定拿所属稀有度的权重，不按组内数量均分。
        foreach (var prize in pool)
        {
            var expected = prize.RarityLabel switch
            {
                "常见" => 58d,
                "少见" => 24d,
                "稀有" => 9d,
                "史诗" => 6d,
                "传说" => 3d,
                _ => 0d
            };
            Assert.AreEqual(expected, prize.Weight, 0.0001, prize.RarityLabel);
        }

        Assert.IsTrue(pool.All(p => p.Name.EndsWith(" ×1")));
    }

    [TestMethod]
    public void EmptyItemListYieldsEmptyPool() =>
        Assert.AreEqual(0, LotteryPrizeCatalog.BuildFromItems([]).Count);

    [TestMethod]
    public void LaunchGrantsOneDrawAndConsumingReducesIt()
    {
        var path = Path.Combine(_root, "state.json");
        Assert.AreEqual(0, LotteryStore.Load(path).AvailableDraws);

        LotteryStore.RecordLaunch(path);
        LotteryStore.RecordLaunch(path);
        Assert.AreEqual(2, LotteryStore.Load(path).AvailableDraws);

        Assert.IsTrue(LotteryStore.TryConsumeDraws(1, out var left, path));
        Assert.AreEqual(1, left);
        Assert.IsFalse(LotteryStore.TryConsumeDraws(10, out var remain, path));
        Assert.AreEqual(1, remain);
        Assert.AreEqual(1, LotteryStore.Load(path).AvailableDraws);
    }

    [TestMethod]
    public void PrizesAccumulatePerId()
    {
        var path = Path.Combine(_root, "state.json");
        LotteryStore.RecordPrizes(["diamond", "diamond", "emerald"], path);
        var state = LotteryStore.Load(path);
        Assert.AreEqual(2, state.CountOf("diamond"));
        Assert.AreEqual(1, state.CountOf("emerald"));
        Assert.AreEqual(0, state.CountOf("unknown"));
        Assert.AreEqual(2, state.OwnedKinds);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public void CorruptedStateFallsBackToEmpty()
    {
        var path = Path.Combine(_root, "broken.json");
        File.WriteAllText(path, "{ not json");
        var state = LotteryStore.Load(path);
        Assert.AreEqual(0, state.LaunchCount);
        Assert.AreEqual(0, state.AvailableDraws);
    }

    [TestMethod]
    public void DrawRequiresPoolAndDraws()
    {
        var service = new LotteryService(Path.Combine(_root, "state.json"));
        Assert.IsFalse(service.TryDrawSingle(out _, out var noPool));
        Assert.IsTrue(noPool.Contains("奖池") || noPool.Contains("目录"));

        service.SetPool(LotteryPrizeCatalog.BuildFallback());
        Assert.IsFalse(service.TryDrawSingle(out _, out var noDraw));
        Assert.IsTrue(noDraw.Contains("次数不足"));
    }

    [TestMethod]
    public void DrawConsumesDrawAndRecordsPrize()
    {
        var path = Path.Combine(_root, "state.json");
        var service = new LotteryService(path, new Random(42));
        service.SetPool(LotteryPrizeCatalog.BuildFallback());
        LotteryStore.RecordLaunch(path);

        Assert.IsTrue(service.TryDrawSingle(out var prize, out var error), error);
        Assert.IsFalse(string.IsNullOrEmpty(prize.Id));

        var state = LotteryStore.Load(path);
        Assert.AreEqual(0, state.AvailableDraws);
        Assert.AreEqual(1, state.PrizeCounts.Values.Sum());
    }

    [TestMethod]
    public void TenDrawNeedsTenChances()
    {
        var path = Path.Combine(_root, "state.json");
        var service = new LotteryService(path, new Random(7));
        service.SetPool(LotteryPrizeCatalog.BuildFallback());
        for (var i = 0; i < 9; i++) LotteryStore.RecordLaunch(path);

        Assert.IsFalse(service.TryDrawTen(out _, out _));
        LotteryStore.RecordLaunch(path);
        Assert.IsTrue(service.TryDrawTen(out var prizes, out var error), error);
        Assert.AreEqual(10, prizes.Count);
        Assert.AreEqual(0, LotteryStore.Load(path).AvailableDraws);
        Assert.AreEqual(10, LotteryStore.Load(path).PrizeCounts.Values.Sum());
    }

    [TestMethod]
    public void CollectionProgressCountsDistinctPrizes()
    {
        var path = Path.Combine(_root, "state.json");
        var service = new LotteryService(path);
        var pool = LotteryPrizeCatalog.BuildFallback();
        service.SetPool(pool);

        LotteryStore.RecordPrizes([pool[0].Id, pool[0].Id, pool[1].Id], path);
        var (owned, total, percent) = service.GetCollectionProgress();
        Assert.AreEqual(2, owned);
        Assert.AreEqual(pool.Count, total);
        Assert.AreEqual(200d / pool.Count, percent, 0.001);
    }

    [TestMethod]
    public void BuildQuadsForGivesFacesInsideCanvas()
    {
        var quads = PCL.Core.Tools.Lottery.Items.MinecraftItemCatalog.BuildQuadsFor("minecraft:bricks", 32);
        if (quads is null || quads.Count == 0)
        {
            Assert.Inconclusive("本机没有缓存的客户端 jar，跳过几何检查");
            return;
        }

        foreach (var quad in quads)
        {
            Assert.AreEqual(4, quad.Points.Length);
            foreach (var point in quad.Points)
            {
                Assert.IsTrue(point.X >= -1f);
                Assert.IsTrue(point.X <= 33f);
                Assert.IsTrue(point.Y >= -1f);
                Assert.IsTrue(point.Y <= 33f);
            }
        }

        // 立方体轮廓：高应当大于宽（原版比例）。
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var quad in quads)
        foreach (var point in quad.Points)
        {
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        }

        Assert.IsTrue(maxY - minY > maxX - minX, "立方体轮廓应当高于宽");
    }
}
