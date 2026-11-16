using System;
using System.Collections.Generic;

namespace PCL.Core.Tools.Lottery;

/// <summary>抽奖状态：累计启动次数、已用抽奖次数、各奖品获得数量。</summary>
public sealed class LotteryState
{
    /// <summary>游戏成功启动的累计次数，每次 +1 抽奖机会。</summary>
    public int LaunchCount { get; set; }

    /// <summary>已经消耗掉的抽奖次数。</summary>
    public int DrawsUsed { get; set; }

    /// <summary>按奖品 Id 累计的获得数量。</summary>
    public Dictionary<string, int> PrizeCounts { get; set; } = new(StringComparer.Ordinal);

    public DateTimeOffset UpdatedAt { get; set; }

    public int AvailableDraws => Math.Max(0, LaunchCount - DrawsUsed);

    public int OwnedKinds => PrizeCounts.Count;

    public int CountOf(string prizeId) =>
        PrizeCounts.TryGetValue(prizeId, out var count) ? count : 0;
}
