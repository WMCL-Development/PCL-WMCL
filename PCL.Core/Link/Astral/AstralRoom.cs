using System.Collections.Generic;

namespace PCL.Core.Link.Astral;

/// <summary>Astral 房间参数（对应 EasyTier 的 network-name / secret / peers）。</summary>
public sealed record AstralRoom(
    string Name,
    string NetworkName,
    string NetworkSecret,
    IReadOnlyList<string> Peers,
    string Source)
{
    /// <summary>AstralGame 6 位短码；有值时需向短码服务换取房间参数。</summary>
    public string? ShortCode { get; init; }

    /// <summary>受保护房间（带密码）。</summary>
    public bool IsProtected { get; init; }

    /// <summary>需要先解析短码才能拿到网络名与密钥。</summary>
    public bool NeedsResolve =>
        !string.IsNullOrWhiteSpace(ShortCode) && string.IsNullOrWhiteSpace(NetworkName);
}
