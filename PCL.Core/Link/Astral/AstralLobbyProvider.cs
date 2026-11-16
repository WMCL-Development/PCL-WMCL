using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Link.Lobby;

namespace PCL.Core.Link.Astral;

/// <summary>
///     Astral 联机方式：识别长链 / 短链 / 短码 / 离线邀请，
///     短码通过 <see cref="AstralShareCodeService" /> 换取房间参数。
/// </summary>
public sealed class AstralLobbyProvider(AstralShareCodeService? service = null) : ILobbyProvider
{
    /// <summary>Astral 房主固定虚拟 IP。</summary>
    public const string HostVirtualIp = "10.144.144.1";

    private readonly AstralShareCodeService _service = service ?? new AstralShareCodeService();

    public string Id => "astral";

    public string DisplayNameKey => "Link.Astral.Name";

    public bool RequiresAccount => false;

    public bool CanParse(string input) => AstralCode.TryParse(input, out _, out _);

    public async Task<LobbyResolution> ResolveAsync(string input, CancellationToken ct = default)
    {
        if (!AstralCode.TryParse(input, out var room, out var error) || room is null)
            return LobbyResolution.Fail(error ?? "无法解析该 Astral 邀请。");

        if (room.NeedsResolve)
        {
            try
            {
                room = await _service.ResolveAsync(room.ShortCode!, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return LobbyResolution.Fail("短码换取失败：" + exception.Message);
            }
        }

        // 环回地址无法与其他机器互连，提前剔除。
        var peers = room.Peers
            .Where(peer => !peer.Contains("127.0.0.1", StringComparison.Ordinal) &&
                           !peer.Contains("localhost", StringComparison.OrdinalIgnoreCase) &&
                           !peer.Contains("[::1]", StringComparison.Ordinal))
            .ToList();

        return LobbyResolution.Ok(new LobbyRoomInfo
        {
            ProviderId = Id,
            Input = input.Trim(),
            DisplayName = room.Name,
            NetworkName = room.NetworkName,
            NetworkSecret = room.NetworkSecret,
            Peers = peers,
            RequiresTun = true,
            // 加入方不指定虚拟 IP：由 EasyTier 分配，避免和房主抢同一个地址。
            VirtualIp = null,
            Port = 25565,
            IsProtected = room.IsProtected,
            Code = room.ShortCode,
            ShareLink = string.IsNullOrWhiteSpace(room.ShortCode)
                ? null
                : AstralCode.ShortLink(room.ShortCode!),
            NeedsResolve = false
        });
    }
}
