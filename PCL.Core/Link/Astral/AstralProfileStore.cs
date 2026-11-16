using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PCL.Core.App;
using PCL.Core.Logging;

namespace PCL.Core.Link.Astral;

/// <summary>
///     房间列表与自定义中继的持久化（同一个 astral.json）。
///     只存房间参数（网络名/密钥/节点），不涉及账号信息；房间最多保留 20 条。
/// </summary>
public static class AstralProfileStore
{
    /// <summary>房间列表上限，超出后丢弃最旧的。</summary>
    public const int MaxRooms = 20;

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string FilePath => Path.Combine(Paths.SharedData, "astral.json");

    public static IReadOnlyList<AstralRoomEntry> Load()
    {
        lock (Gate) return Read().Rooms;
    }

    /// <summary>插入或更新一条记录（按网络名+密钥去重），返回更新后的列表。</summary>
    public static IReadOnlyList<AstralRoomEntry> Remember(AstralRoom room)
    {
        lock (Gate)
        {
            var payload = Read();
            var rooms = payload.Rooms.ToList();
            rooms.RemoveAll(entry =>
                string.Equals(entry.NetworkName, room.NetworkName, StringComparison.Ordinal) &&
                string.Equals(entry.NetworkSecret, room.NetworkSecret, StringComparison.Ordinal));
            rooms.Insert(0, AstralRoomEntry.FromRoom(room));
            if (rooms.Count > MaxRooms) rooms.RemoveRange(MaxRooms, rooms.Count - MaxRooms);
            payload.Rooms = rooms;
            Write(payload);
            return rooms;
        }
    }

    /// <summary>移除一条记录，返回更新后的列表。</summary>
    public static IReadOnlyList<AstralRoomEntry> Remove(AstralRoomEntry entry)
    {
        lock (Gate)
        {
            var payload = Read();
            var rooms = payload.Rooms.ToList();
            rooms.RemoveAll(item =>
                string.Equals(item.NetworkName, entry.NetworkName, StringComparison.Ordinal) &&
                string.Equals(item.NetworkSecret, entry.NetworkSecret, StringComparison.Ordinal));
            payload.Rooms = rooms;
            Write(payload);
            return rooms;
        }
    }

    public static IReadOnlyList<AstralRelay> LoadRelays()
    {
        lock (Gate) return Read().Relays;
    }

    public static void SaveRelays(IReadOnlyList<AstralRelay> relays)
    {
        lock (Gate)
        {
            var payload = Read();
            payload.Relays = [.. relays];
            Write(payload);
        }
    }

    private static ProfilePayload Read()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return new ProfilePayload();
            return JsonSerializer.Deserialize<ProfilePayload>(File.ReadAllText(path))
                   ?? new ProfilePayload();
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            LogWrapper.Warn("Astral", "astral.json 读取失败，按空处理：" + exception.Message);
            return new ProfilePayload();
        }
    }

    private static void Write(ProfilePayload payload)
    {
        try
        {
            var path = FilePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, JsonSerializer.Serialize(payload, Options));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            LogWrapper.Warn("Astral", "astral.json 写入失败：" + exception.Message);
        }
    }

    private sealed class ProfilePayload
    {
        public List<AstralRoomEntry> Rooms { get; set; } = [];
        public List<AstralRelay> Relays { get; set; } = [];
    }
}
