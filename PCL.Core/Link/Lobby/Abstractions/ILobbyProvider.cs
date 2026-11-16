using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Link.Lobby;

/// <summary>解析结果：成功时带房间参数，失败时带可直接展示的原因。</summary>
public sealed record LobbyResolution(bool Success, LobbyRoomInfo? Room, string? Error)
{
    public static LobbyResolution Ok(LobbyRoomInfo room) => new(true, room, null);

    public static LobbyResolution Fail(string error) => new(false, null, error);
}

/// <summary>
///     联机方式插件。每种方式只负责「识别输入 + 解析成房间参数」，
///     EasyTier 启动与连接流程由调用方统一处理。
/// </summary>
public interface ILobbyProvider
{
    /// <summary>稳定标识，用于配置持久化。</summary>
    string Id { get; }

    /// <summary>本地化键，界面显示用。</summary>
    string DisplayNameKey { get; }

    /// <summary>该方式是否需要登录账号（大厅需要，Astral 不需要）。</summary>
    bool RequiresAccount { get; }

    /// <summary>是否能同步识别这段输入（用于界面提前校验、切换输入框提示）。</summary>
    bool CanParse(string input);

    /// <summary>判断是否属于本方式；属于则解析出房间参数。</summary>
    Task<LobbyResolution> ResolveAsync(string input, CancellationToken ct = default);
}
