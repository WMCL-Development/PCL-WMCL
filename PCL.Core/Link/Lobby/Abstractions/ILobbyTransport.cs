using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Link.Lobby;

/// <summary>连接结果：失败时 <see cref="Error" /> 可直接展示。</summary>
public sealed record LobbyConnectResult(bool Success, string? Error)
{
    public static LobbyConnectResult Ok() => new(true, null);

    public static LobbyConnectResult Fail(string error) => new(false, error);
}

/// <summary>
///     把解析好的房间参数真正连起来。大厅走 Scaffolding 控制通道，
///     Astral 走 tun 直连，二者由 provider 标识分派。
/// </summary>
public interface ILobbyTransport
{
    /// <summary>与 <see cref="ILobbyProvider.Id" /> 对应的标识。</summary>
    string ProviderId { get; }

    Task<LobbyConnectResult> JoinAsync(
        LobbyRoomInfo room,
        string username,
        CancellationToken ct = default);

    Task LeaveAsync(CancellationToken ct = default);

    /// <summary>查询当前房间成员（EasyTier peer 表原文）；不支持时返回 null。</summary>
    Task<string?> QueryPeersAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}

/// <summary>传输层注册表与分派。</summary>
public static class LobbyConnector
{
    private static readonly List<ILobbyTransport> Transports = [];

    public static IReadOnlyList<ILobbyTransport> All => Transports;

    public static void Register(ILobbyTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        Transports.RemoveAll(existing => existing.ProviderId == transport.ProviderId);
        Transports.Add(transport);
    }

    public static void Reset() => Transports.Clear();

    public static ILobbyTransport? Find(string providerId) =>
        Transports.Find(transport => transport.ProviderId == providerId);

    public static async Task<LobbyConnectResult> JoinAsync(
        LobbyRoomInfo room,
        string username,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(room);
        var transport = Find(room.ProviderId);
        if (transport is null)
            return LobbyConnectResult.Fail($"未注册的联机方式：{room.ProviderId}");

        return await transport.JoinAsync(room, username, ct).ConfigureAwait(false);
    }

    /// <summary>读取指定方式当前的成员表。</summary>
    public static async Task<string?> QueryPeersAsync(
        string providerId,
        CancellationToken ct = default)
    {
        var transport = Find(providerId);
        if (transport is null) return null;
        try
        {
            return await transport.QueryPeersAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static async Task LeaveAllAsync(CancellationToken ct = default)
    {
        foreach (var transport in Transports.ToArray())
        {
            try
            {
                await transport.LeaveAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 退出阶段的异常不向上抛，避免阻塞其他清理。
            }
        }
    }
}
