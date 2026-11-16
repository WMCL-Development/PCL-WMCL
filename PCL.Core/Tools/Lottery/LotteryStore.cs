using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PCL.Core.App;

namespace PCL.Core.Tools.Lottery;

/// <summary>
/// 抽奖状态存储。抽奖次数与启动次数绑定：每成功启动一次游戏 +1 次机会。
/// 写入走临时文件 + 覆盖，避免半截文件。
/// </summary>
public static class LotteryStore
{
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string DefaultPath =>
        Path.Combine(Paths.SharedData, "lottery-state.json");

    public static LotteryState Load(string? path = null)
    {
        lock (Gate) return LoadUnlocked(path ?? DefaultPath);
    }

    /// <summary>游戏进程成功启动后调用；返回更新后的状态。</summary>
    public static LotteryState RecordLaunch(string? path = null)
    {
        lock (Gate)
        {
            var file = path ?? DefaultPath;
            var state = LoadUnlocked(file);
            state.LaunchCount++;
            SaveUnlocked(file, state);
            return state;
        }
    }

    /// <summary>消耗抽奖次数；次数不足时返回 false 并给出剩余次数。</summary>
    public static bool TryConsumeDraws(int count, out int available, string? path = null)
    {
        lock (Gate)
        {
            var file = path ?? DefaultPath;
            var state = LoadUnlocked(file);
            available = state.AvailableDraws;
            if (count <= 0 || available < count) return false;
            state.DrawsUsed += count;
            SaveUnlocked(file, state);
            available = state.AvailableDraws;
            return true;
        }
    }

    /// <summary>记录抽到的奖品，按 Id 累加。</summary>
    public static LotteryState RecordPrizes(IEnumerable<string> prizeIds, string? path = null)
    {
        lock (Gate)
        {
            var file = path ?? DefaultPath;
            var state = LoadUnlocked(file);
            foreach (var id in prizeIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                state.PrizeCounts[id] = state.CountOf(id) + 1;
            }
            SaveUnlocked(file, state);
            return state;
        }
    }

    private static LotteryState LoadUnlocked(string file)
    {
        try
        {
            if (!File.Exists(file)) return new LotteryState();
            var state = JsonSerializer.Deserialize<LotteryState>(File.ReadAllText(file));
            if (state is null) return new LotteryState();
            state.PrizeCounts = new Dictionary<string, int>(
                state.PrizeCounts ?? [], StringComparer.Ordinal);
            return state;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // 文件损坏时按全新状态处理，不阻断启动。
            return new LotteryState();
        }
    }

    private static void SaveUnlocked(string file, LotteryState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            state.UpdatedAt = DateTimeOffset.Now;
            var temporary = file + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // 写入失败不影响游戏启动。
        }
    }
}
