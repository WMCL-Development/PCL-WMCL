using System;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Link.Lobby;

/// <summary>
///     PCL CE / 陶瓦大厅编号：解析为 EasyTier 网络参数，连接时走 Scaffolding 控制通道，
///     因此不需要 tun，也不需要手动填端口。
/// </summary>
public sealed class ScaffoldingLobbyProvider : ILobbyProvider
{
    public string Id => "scaffolding";

    public string DisplayNameKey => "Link.Lobby.Name";

    public bool RequiresAccount => true;

    public bool CanParse(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        // 先做便宜的语法预判：大厅编号是 9 位以上、只含大写字母/数字/连字符的串。
        // 链接、短码、带空格的内容在这里就被排除，避免把解析器里的错误日志当成异常抛给用户。
        var trimmed = input.Trim();
        if (trimmed.Length < 9) return false;
        if (trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.Contains('/') || trimmed.Contains('?') ||
            trimmed.Contains('.') || trimmed.Contains(' ') ||
            trimmed.Contains('\n'))
            return false;
        foreach (var ch in trimmed)
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '-')
                return false;

        try
        {
            return LobbyInfoProvider.ParseCode(trimmed) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Task<LobbyResolution> ResolveAsync(string input, CancellationToken ct = default)
    {
        LobbyInfoProvider.LobbyInfo? info;
        try
        {
            info = LobbyInfoProvider.ParseCode(input);
        }
        catch (Exception)
        {
            info = null;
        }

        if (info is null)
            return Task.FromResult(LobbyResolution.Fail("大厅编号无效，请检查是否输入完整。"));

        return Task.FromResult(LobbyResolution.Ok(new LobbyRoomInfo
        {
            ProviderId = Id,
            Input = input.Trim(),
            DisplayName = info.OriginalCode,
            NetworkName = info.NetworkName,
            NetworkSecret = info.NetworkSecret,
            Peers = [],
            RequiresTun = false,
            VirtualIp = info.Ip,
            Port = info.Port,
            Code = info.OriginalCode,
            NeedsResolve = false
        }));
    }
}
