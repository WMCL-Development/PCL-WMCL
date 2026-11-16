using System.Collections.Generic;

namespace PCL.Core.Link.Lobby;

/// <summary>
///     一次联机房间解析结果。大厅（Scaffolding）与 Astral 共用，
///     区别在于是否带虚拟 IP、是否需要端口转发、是否有控制通道。
/// </summary>
public sealed record LobbyRoomInfo
{
    /// <summary>来源 provider 标识，例如 scaffolding / astral。</summary>
    public required string ProviderId { get; init; }

    /// <summary>用户输入的原始内容（编号、短码、链接）。</summary>
    public required string Input { get; init; }

    /// <summary>房间显示名。</summary>
    public required string DisplayName { get; init; }

    /// <summary>EasyTier network-name。</summary>
    public required string NetworkName { get; init; }

    /// <summary>EasyTier network-secret。</summary>
    public required string NetworkSecret { get; init; }

    /// <summary>自定义节点/中继；为空表示交给 EasyTier 公共节点。</summary>
    public IReadOnlyList<string> Peers { get; init; } = [];

    /// <summary>是否需要 tun 虚拟网卡（Astral 为 true，大厅为 false）。</summary>
    public bool RequiresTun { get; init; }

    /// <summary>连接时使用的虚拟 IP；Astral 房主固定 10.144.144.1。</summary>
    public string? VirtualIp { get; init; }

    /// <summary>Minecraft 端口；大厅由控制通道查询，Astral 需手动填。</summary>
    public int Port { get; init; } = 25565;

    /// <summary>是否为受保护房间（带密码）。</summary>
    public bool IsProtected { get; init; }

    /// <summary>短码（Astral）/ 大厅编号。</summary>
    public string? Code { get; init; }

    /// <summary>可直接分享的链接（Astral 长链）。</summary>
    public string? ShareLink { get; init; }

    /// <summary>房间是否还需要向远端换取参数（例如 Astral 6 位短码）。</summary>
    public bool NeedsResolve { get; init; }
}
