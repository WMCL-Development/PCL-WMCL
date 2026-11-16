using System;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Logging;
using PCL.Core.Utils.Secret;

namespace PCL.Core.Link.Astral;

/// <summary>
///     Astral 联机的传输层：直接起 EasyTier 并保持 tun，
///     玩家用房主虚拟 IP 进游戏，不做端口转发也广播。
/// </summary>
public sealed class AstralLobbyTransport : ILobbyTransport
{
    private readonly Func<EasyTierOptions, IEasyTierProcess> _factory;
    private IEasyTierProcess? _entity;

    public AstralLobbyTransport(Func<EasyTierOptions, IEasyTierProcess>? factory = null)
    {
        _factory = factory ?? (options => new EasyTierEntity(options));
    }

    public string ProviderId => "astral";

    public async Task<LobbyConnectResult> JoinAsync(
        LobbyRoomInfo room,
        string username,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(room);
        ct.ThrowIfCancellationRequested();

        if (!string.Equals(room.ProviderId, ProviderId, StringComparison.Ordinal))
            return LobbyConnectResult.Fail("房间不属于 Astral 联机方式。");

        if (string.IsNullOrWhiteSpace(room.NetworkName))
            return LobbyConnectResult.Fail("房间缺少网络名，请重新获取邀请。");

        // Astral 依赖真实 tun 网卡：Linux 上先确认 /dev/net/tun 与 CAP_NET_ADMIN。
        if (room.RequiresTun && !await EnsureTunAsync(ct).ConfigureAwait(false))
            return LobbyConnectResult.Fail(EasyTierElevation.TunUnavailableMessage);

        var options = new EasyTierOptions
        {
            NetworkName = room.NetworkName,
            NetworkSecret = room.NetworkSecret,
            Peers = room.Peers,
            FullCode = room.Code ?? room.Input,
            // Astral 需要真实虚拟网卡与中继，不能沿用大厅的 no-tun / p2p-only。
            NoTun = false,
            P2POnly = false,
            PrivateMode = false,
            AsHost = false,
            VirtualIp = string.IsNullOrWhiteSpace(room.VirtualIp) ? null : room.VirtualIp,
            MinecraftPort = room.Port,
            Hostname = string.IsNullOrWhiteSpace(username) ? null : username,
            MachineId = Identify.LauncherId,
            DisableHolePunching = EasyTierDefaults.DisableHolePunching,
            DisableIpv6 = EasyTierDefaults.DisableIpv6,
            LatencyFirst = EasyTierDefaults.LatencyFirst,
            DefaultProtocol = EasyTierDefaults.DefaultProtocol
        };

        try
        {
            if (_entity is not null) await _entity.StopAsync().ConfigureAwait(false);
            _entity = _factory(options);
            var code = _entity.Launch();
            if (code != 0)
                return LobbyConnectResult.Fail("EasyTier 启动失败，请检查是否已安装或被占用。");

            LogWrapper.Info("Astral", $"已加入房间 {room.DisplayName}（网络 {room.NetworkName}）");
            return LobbyConnectResult.Ok();
        }
        catch (Exception exception)
        {
            LogWrapper.Error(exception, "Astral", "加入房间失败");
            return LobbyConnectResult.Fail(exception.Message);
        }
    }

    /// <summary>EasyTier 尚未安装时跳过检查，交给安装流程处理。</summary>
    private static async Task<bool> EnsureTunAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) return true;
        if (!EasyTierDefaults.IsInstalled) return true;
        return await EasyTierElevation.EnsureTunReadyAsync(ct: ct).ConfigureAwait(false);
    }

    public async Task<string?> QueryPeersAsync(CancellationToken ct = default)
    {
        var entity = _entity;
        return entity is null ? null : await entity.QueryPeersAsync().ConfigureAwait(false);
    }

    public async Task LeaveAsync(CancellationToken ct = default)
    {
        var entity = _entity;
        _entity = null;
        if (entity is null) return;
        await entity.StopAsync().ConfigureAwait(false);
    }
}
