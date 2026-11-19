

using System.Collections.Generic;
using System;

namespace PCL.Core.Link.EasyTier;

/// <summary>
///     EasyTier 进程的启动参数。大厅（Scaffolding）与 Astral 共用这套描述，
///     差别只在 tun、虚拟 IP、节点列表与白名单。
/// </summary>
public sealed record EasyTierOptions
{
            /// <summary>网络名（EasyTier network-name）。</summary>
    public required string NetworkName { get; init; }

    /// <summary>网络密钥（EasyTier network-secret）。</summary>
    public required string NetworkSecret { get; init; }

    /// <summary>自定义节点/中继；为空时由调用方决定回退到公共节点。</summary>
    public IReadOnlyList<string> Peers { get; init; } = [];

    /// <summary>是否禁用 tun 网卡。大厅走 TCP 转发因此为 true；Astral 需要 tun 才能拿到虚拟 IP。</summary>
    public bool NoTun { get; init; } = true;

    /// <summary>房主侧的虚拟 IP；为 null 时不传 <c>-i</c>。</summary>
    public string? VirtualIp { get; init; }

    /// <summary>private-mode，Astral 房间未必开启。</summary>
    public bool PrivateMode { get; init; } = true;

    /// <summary>p2p-only。Astral 允许中继，需为 false。</summary>
    public bool P2POnly { get; init; } = true;

    public bool MultiThread { get; init; } = true;

    public bool EnableKcpProxy { get; init; } = true;

    public bool EnableQuicProxy { get; init; } = true;

    /// <summary>对应 EasyTier 的 disable-sys-hole-punching（与 PCL 的“尝试穿透”设置相反）。</summary>
    public bool DisableHolePunching { get; init; }

    public bool DisableIpv6 { get; init; }

    public bool LatencyFirst { get; init; }

    /// <summary>机器唯一标识，写入 machine-id。</summary>
    public string MachineId { get; init; } = "";

    /// <summary>是否为房主：影响监听白名单与 hostname。</summary>
    public bool AsHost { get; init; }

    /// <summary>Minecraft 端口；房主会把它加入 tcp/udp 白名单。</summary>
    public int MinecraftPort { get; init; }

    /// <summary>控制通道端口；房主会把它加入白名单。</summary>
    public int ControlPort { get; init; }

    /// <summary>入站监听地址，默认全协议零端口。</summary>
    public IReadOnlyList<string> Listeners { get; init; } = ["tcp://0.0.0.0:0", "udp://0.0.0.0:0"];

    /// <summary>默认协议（tcp/udp/ws/wss）。</summary>
    public string DefaultProtocol { get; init; } = "tcp";

    /// <summary>加入方若不指定 hostname，则由宿主生成随机值。</summary>
    public string? Hostname { get; init; }

    /// <summary>
    ///     端口转发规则，形如 <c>tcp://127.0.0.1:25565/10.126.126.1:25565</c>。
    ///     无 tun 的平台（Android）靠它把本地端口映射到虚拟网络里的房主端口。
    /// </summary>
    public IReadOnlyList<string> PortForwards { get; init; } = [];

    /// <summary>
    ///     加入方自动端口转发的本地端口；为 null 表示不自动转发。
    ///     无 tun 的平台（Android）靠它把本地端口接到房主的 Minecraft 端口。
    /// </summary>
    public int? AutoForwardLocalPort { get; init; }

    /// <summary>有 tun 能力时使用的虚拟 IPv4（Android JNI 模式）。</summary>
    public string? AutoTunIpv4 { get; init; } = "10.126.126.2";

    /// <summary>自动转发的远端端口，默认 Minecraft 的 25565。</summary>
    public int AutoForwardRemotePort { get; init; } = 25565;

    /// <summary>房间原始编号，仅用于回显（大厅编号 / Astral 短码或长链）。</summary>
    public string? FullCode { get; init; }
}
