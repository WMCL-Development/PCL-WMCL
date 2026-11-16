using System;
using System.Collections.Generic;

namespace PCL.Core.Link.Astral;

/// <summary>房间列表里的一条记录：加入过或手动添加的 Astral 房间。</summary>
public sealed record AstralRoomEntry(
    string Name,
    string NetworkName,
    string NetworkSecret,
    IReadOnlyList<string> Peers,
    string Source,
    DateTimeOffset SavedAt)
{
    /// <summary>AstralGame 短码；有值时可重新向服务换取参数。</summary>
    public string? ShortCode { get; init; }

    /// <summary>房间是否带密码。</summary>
    public bool IsProtected { get; init; }

    /// <summary>列表里显示的第二行文字。</summary>
    public string Summary =>
        $"{Source} · 节点 {Peers.Count} 个" +
        (string.IsNullOrWhiteSpace(ShortCode) ? "" : $" · 短码 {ShortCode}");

    public AstralRoom ToRoom() =>
        new(Name, NetworkName, NetworkSecret, Peers, Source)
        {
            ShortCode = ShortCode,
            IsProtected = IsProtected
        };

    public static AstralRoomEntry FromRoom(AstralRoom room) =>
        new(room.Name, room.NetworkName, room.NetworkSecret, [.. room.Peers], room.Source,
            DateTimeOffset.Now)
        {
            ShortCode = room.ShortCode,
            IsProtected = room.IsProtected
        };
}
