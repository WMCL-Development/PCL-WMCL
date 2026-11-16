using System;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;

namespace PCL.Core.Link.Lobby;

/// <summary>
///     大厅传输层：沿用 Scaffolding 控制通道，负责查询端口、端口转发与局域网广播。
/// </summary>
public sealed class ScaffoldingLobbyTransport : ILobbyTransport
{
    private readonly LobbyController _controller = new();

    public string ProviderId => "scaffolding";

    public async Task<LobbyConnectResult> JoinAsync(
        LobbyRoomInfo room,
        string username,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(room);
        var code = room.Code ?? room.Input;
        if (string.IsNullOrWhiteSpace(code))
            return LobbyConnectResult.Fail("缺少大厅编号。");

        try
        {
            var entity = await _controller.LaunchClientAsync(username, code, ct).ConfigureAwait(false);
            if (entity is null)
                return LobbyConnectResult.Fail("大厅连接失败，请检查网络或编号是否有效。");

            LogWrapper.Info("Lobby", $"已加入大厅 {code}");
            return LobbyConnectResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogWrapper.Error(exception, "Lobby", "加入大厅失败");
            return LobbyConnectResult.Fail(exception.Message);
        }
    }

    public async Task LeaveAsync(CancellationToken ct = default)
    {
        await _controller.CloseAsync().ConfigureAwait(false);
    }
}
