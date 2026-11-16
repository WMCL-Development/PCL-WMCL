using System;
using System.Collections.Generic;
using System.Linq;

namespace PCL.Core.Tools.Lottery;

/// <summary>
/// 抽奖业务：持有奖池与状态文件，负责单抽 / 十连、扣次数与记录奖品。
/// 随机源可注入，便于测试固定结果。
/// </summary>
public sealed class LotteryService
{
    private readonly string? _statePath;
    private readonly Random _random;
    private IReadOnlyList<LotteryPrize> _pool = [];

    public LotteryService(string? statePath = null, Random? random = null)
    {
        _statePath = statePath;
        _random = random ?? Random.Shared;
    }

    public IReadOnlyList<LotteryPrize> PrizePool => _pool;

    public LotteryState State => LotteryStore.Load(_statePath);

    public int AvailableDraws => State.AvailableDraws;

    public bool IsPoolReady => _pool.Count > 0;

    /// <summary>从插件目录（原版 jar，可附带 Mod jar）重建奖池。</summary>
    public void LoadPool(
        string? gameDirectory,
        string? cachePath = null,
        IReadOnlyCollection<string>? modJars = null,
        IReadOnlyList<string>? clientJars = null,
        int iconSize = 32)
    {
        _pool = LotteryPrizeCatalog.Build(gameDirectory, cachePath, modJars, clientJars, iconSize);
    }

    /// <summary>直接用给定物品重建奖池（测试与外部调用）。</summary>
    public void SetPool(IReadOnlyList<LotteryPrize> prizes) => _pool = prizes;

    public bool TryDrawSingle(out LotteryPrize prize, out string error)
    {
        if (_pool.Count == 0)
        {
            prize = LotteryPrize.Empty;
            error = "奖品目录尚未加载完成";
            return false;
        }
        if (!LotteryStore.TryConsumeDraws(1, out var available, _statePath))
        {
            prize = _pool[0];
            error = $"抽奖次数不足（当前 {available} 次）";
            return false;
        }
        prize = Pick();
        LotteryStore.RecordPrizes([prize.Id], _statePath);
        error = "";
        return true;
    }

    public bool TryDrawTen(out IReadOnlyList<LotteryPrize> prizes, out string error)
    {
        if (_pool.Count == 0)
        {
            prizes = [];
            error = "奖品目录尚未加载完成";
            return false;
        }
        if (!LotteryStore.TryConsumeDraws(10, out var available, _statePath))
        {
            prizes = [];
            error = $"十连抽需要 10 次机会（当前 {available} 次）";
            return false;
        }
        prizes = Enumerable.Range(0, 10).Select(_ => Pick()).ToArray();
        LotteryStore.RecordPrizes(prizes.Select(prize => prize.Id), _statePath);
        error = "";
        return true;
    }

    /// <summary>按权重抽取一次；权重为零全部落空时取末尾项。</summary>
    public LotteryPrize Pick()
    {
        if (_pool.Count == 0) return LotteryPrize.Empty;
        var total = _pool.Sum(prize => prize.Weight);
        if (total <= 0) return _pool[^1];
        var roll = _random.NextDouble() * total;
        foreach (var prize in _pool)
        {
            if (roll < prize.Weight) return prize;
            roll -= prize.Weight;
        }
        return _pool[^1];
    }

    /// <summary>收藏进度：已收集种类 / 奖池种类，以及百分比。</summary>
    public (int Owned, int Total, double Percent) GetCollectionProgress()
    {
        var state = State;
        var total = _pool.Count;
        if (total == 0) return (0, 0, 0);
        var owned = _pool.Count(prize => state.CountOf(prize.Id) > 0);
        return (owned, total, owned * 100d / total);
    }
}
