using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Logging;
using PCL.Core.Utils.Secret;

namespace PCL.Core.Link.Astral;

/// <summary>开房结果：长链总是可用，短码失败时退化为仅长链。</summary>
public sealed record AstralHostResult(
    bool Success,
    AstralRoom? Room,
    string? LongLink,
    string? ShortLink,
    string? ShortCode,
    string? Error)
{
    public static AstralHostResult Fail(string error) => new(false, null, null, null, null, error);
}

/// <summary>
///     Astral 房主流程：生成房间参数 → 起 EasyTier（tun + 固定虚拟 IP）
///     → 生成长链 → 可选申请短码。
/// </summary>
public sealed class AstralLobbyHost
{
    private readonly Func<EasyTierOptions, IEasyTierProcess> _factory;

    private readonly Func<AstralRoom, CancellationToken, Task<string>>? _createCode;
    private readonly IReadOnlyList<string>? _peers;
    private IEasyTierProcess? _process;

    /// <param name="factory">EasyTier 进程工厂，测试可注入替身。</param>
    /// <param name="createCode">短码申请委托；为 null 时不申请短码。</param>
    /// <param name="peers">自定义中继节点；为空使用公共节点。</param>
    public AstralLobbyHost(
        Func<EasyTierOptions, IEasyTierProcess>? factory = null,
        Func<AstralRoom, CancellationToken, Task<string>>? createCode = null,
        IReadOnlyList<string>? peers = null)
    {
        _factory = factory ?? (options => new Scaffolding.EasyTier.EasyTierEntity(options));
        _createCode = createCode;
        _peers = peers;
    }

    /// <summary>用界面上填好的房间参数开房（网络名/密钥/保护标记都可自定义）。</summary>
    public Task<AstralHostResult> StartAsync(
        AstralRoom room,
        string username,
        int minecraftPort = 25565,
        CancellationToken ct = default) =>
        StartCoreAsync(room, username, minecraftPort, ct);

    /// <summary>开房并返回分享用的长链/短码。</summary>
    public Task<AstralHostResult> StartAsync(
        string? roomName,
        string username,
        int minecraftPort = 25565,
        CancellationToken ct = default) =>
        StartCoreAsync(AstralRoomFactory.Create(roomName, _peers), username, minecraftPort, ct);

    private async Task<AstralHostResult> StartCoreAsync(
        AstralRoom room,
        string username,
        int minecraftPort,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (OperatingSystem.IsLinux() && EasyTierDefaults.IsInstalled &&
            !await EasyTierElevation.EnsureTunReadyAsync(ct: ct).ConfigureAwait(false))
            return AstralHostResult.Fail(EasyTierElevation.TunUnavailableMessage);

        var options = new EasyTierOptions
        {
            NetworkName = room.NetworkName,
            NetworkSecret = room.NetworkSecret,
            Peers = room.Peers,
            FullCode = room.NetworkName,
            // 房主必须开 tun 并固定虚拟 IP，其他人才能直连。
            NoTun = false,
            P2POnly = false,
            PrivateMode = false,
            AsHost = true,
            VirtualIp = AstralLobbyProvider.HostVirtualIp,
            MinecraftPort = minecraftPort,
            Hostname = string.IsNullOrWhiteSpace(username) ? null : username,
            MachineId = Identify.LauncherId,
            DisableHolePunching = EasyTierDefaults.DisableHolePunching,
            DisableIpv6 = EasyTierDefaults.DisableIpv6,
            LatencyFirst = EasyTierDefaults.LatencyFirst,
            DefaultProtocol = EasyTierDefaults.DefaultProtocol
        };

        try
        {
            await StopAsync().ConfigureAwait(false);
            _process = _factory(options);
            if (_process.Launch() != 0)
                return AstralHostResult.Fail("EasyTier 启动失败，请检查安装与权限（tun 需要管理员权限）。");
        }
        catch (Exception exception)
        {
            LogWrapper.Error(exception, "Astral", "开房失败");
            return AstralHostResult.Fail(exception.Message);
        }

        var longLink = AstralCode.EncodeRoomLink(
            room.Name, room.NetworkName, room.NetworkSecret, room.Peers, room.IsProtected);

        string? shortCode = null;
        string? shortLink = null;
        if (_createCode is not null)
        {
            try
            {
                shortCode = await _createCode(room, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(shortCode))
                    shortLink = AstralCode.ShortLink(shortCode!);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // 短码服务不可用时仍可分享长链。
                LogWrapper.Warn("Astral", "短码申请失败，仅提供长链：" + exception.Message);
            }
        }

        LogWrapper.Info("Astral", $"已开房 {room.Name}（网络 {room.NetworkName}）");
        return new AstralHostResult(true, room, longLink, shortLink, shortCode, null);
    }

    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            await process.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Astral", "停止房间失败：" + exception.Message);
        }
    }
}
