using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Core.Link.Astral;
using PCL.Core.Link.Lobby;
using PCL.Core.Logging;
using PCL.Core.ViewModel;

namespace PCL;

/// <summary>
///     Astral 联机面板：加入/创建房间、房间列表、中继节点、分享串与连接地址。
///     联机细节在 PCL.Core 的 Provider 抽象里，这里只维护界面状态。
/// </summary>
public partial class PageToolsAstralViewModel : ViewModelBase
{
    private readonly AstralLobbyHost _host = new(
        createCode: static (room, ct) => new AstralShareCodeService().CreateAsync(room, ct));

    private bool _joined;
    private string _joinedNetwork = "";

    public PageToolsAstralViewModel()
    {
        foreach (var entry in AstralProfileStore.Load()) Rooms.Add(entry);
        RefreshRelayChoices();
        Status = Rooms.Count == 0
            ? "还没有加入过房间，可以粘贴邀请或直接开房。"
            : "选择一个房间加入，或粘贴新的邀请。";
    }

    /// <summary>房间列表（持久化，最多 20 条）。</summary>
    public ObservableCollection<AstralRoomEntry> Rooms { get; } = [];

    /// <summary>当前房间成员（轮询 EasyTier peer 表）。</summary>
    public ObservableCollection<AstralMember> Members { get; } = [];

    private CancellationTokenSource? _memberCts;

    /// <summary>开房时可勾选的中继。</summary>
    public ObservableCollection<AstralRelayChoice> RelayChoices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJoinSelected))]
    private AstralRoomEntry? _selectedRoom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJoin))]
    private string _input = "";

    [ObservableProperty]
    private string _preview = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJoin))]
    [NotifyPropertyChangedFor(nameof(CanJoinSelected))]
    [NotifyPropertyChangedFor(nameof(CanLeave))]
    [NotifyPropertyChangedFor(nameof(CanHost))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoom))]
    private string _roomSummary = "";

    [ObservableProperty]
    private string _hostStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShareLink))]
    private string _shareShortLink = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShareLink))]
    private string _shareLongLink = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectAddress))]
    private string _connectAddress = "";

    /// <summary>开房表单：房间名。</summary>
    [ObservableProperty]
    private string _newRoomName = "我的房间";

    /// <summary>开房表单：是否生成随机密码（保护房间）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditNetwork))]
    private bool _isProtectedRoom = true;

    /// <summary>开房表单：自定义网络名（仅不保护房间时可用）。</summary>
    [ObservableProperty]
    private string _newRoomNetwork = "";

    /// <summary>开房表单：自定义密钥（仅不保护房间时可用）。</summary>
    [ObservableProperty]
    private string _newRoomSecret = "";

    public bool HasRoom => !string.IsNullOrWhiteSpace(RoomSummary);

    public bool HasShareLink =>
        !string.IsNullOrWhiteSpace(ShareShortLink) || !string.IsNullOrWhiteSpace(ShareLongLink);

    public bool HasConnectAddress => !string.IsNullOrWhiteSpace(ConnectAddress);

    public bool HasRooms => Rooms.Count > 0;

    public bool HasMembers => Members.Count > 0;

    public bool CanEditNetwork => !IsProtectedRoom;

    public bool CanJoin => !IsBusy && !string.IsNullOrWhiteSpace(Input);

    public bool CanJoinSelected => !IsBusy && SelectedRoom is not null;

    public bool CanLeave => !IsBusy && (_joined || HasRoom);

    public bool CanHost => !IsBusy;

    private string EffectiveUserName =>
        string.IsNullOrWhiteSpace(PCL.Core.App.Config.Link.AstralUsername)
            ? Environment.UserName
            : PCL.Core.App.Config.Link.AstralUsername.Trim();

    private string? EffectiveVirtualIp =>
        string.IsNullOrWhiteSpace(PCL.Core.App.Config.Link.AstralVirtualIp)
            ? null
            : PCL.Core.App.Config.Link.AstralVirtualIp.Trim();

    partial void OnInputChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Preview = "";
            return;
        }

        if (!AstralCode.TryParse(value, out var room, out var error) || room is null)
        {
            Preview = "无法解析：" + (error ?? "未知原因");
            return;
        }

        Preview = room.NeedsResolve
            ? $"短码 {room.ShortCode} · 加入时会向短码服务换取房间参数"
            : AstralCode.UsedFallbackPeers(room)
                ? $"房间：{room.Name} · 来源：{room.Source} · 邀请未带节点，将使用公共中继"
                : $"房间：{room.Name} · 来源：{room.Source} · 节点 {room.Peers.Count} 个";
    }

    [RelayCommand]
    private async Task JoinAsync()
    {
        if (!CanJoin) return;
        IsBusy = true;
        Status = "正在识别邀请…";
        try
        {
            var resolution = await LobbyService.ResolveInputAsync(Input);
            if (!resolution.Success || resolution.Room is null)
            {
                Status = resolution.Error ?? "无法识别该邀请。";
                return;
            }

            var room = resolution.Room;
            if (room.ProviderId != "astral")
            {
                Status = "这个编号属于联机大厅，请切回「联机大厅」加入。";
                return;
            }

            if (EffectiveVirtualIp is { } customIp)
                room = room with { VirtualIp = customIp };

            Status = "正在连接…";
            var result = await LobbyService.JoinByInputAsync(Input, EffectiveUserName);
            if (!result.Success)
            {
                Status = result.Error ?? "加入失败。";
                return;
            }

            ApplyJoined(room.DisplayName, room.NetworkName, room.Code, room.Port);
            ShareLongLink = AstralCode.EncodeRoomLink(
                room.DisplayName, room.NetworkName, room.NetworkSecret, room.Peers);

            RememberRooms(AstralProfileStore.Remember(new AstralRoom(
                room.DisplayName, room.NetworkName, room.NetworkSecret, room.Peers, "Astral")
            {
                ShortCode = room.Code,
                IsProtected = room.IsProtected
            }));

            StartMemberPolling();
            Status = $"已加入 {room.DisplayName}。";
        }
        catch (Exception exception)
        {
            LogWrapper.Error(exception, "Astral", "加入流程异常");
            Status = "加入失败：" + exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    /// <summary>只把邀请记进房间列表，不立即连接。</summary>
    [RelayCommand]
    private async Task AddRoomAsync()
    {
        if (!CanJoin) return;
        IsBusy = true;
        Status = "正在解析邀请…";
        try
        {
            var resolution = await LobbyService.ResolveInputAsync(Input);

            if (!resolution.Success || resolution.Room is null)
            {
                // 短码服务不可用时仍先记下短码，加入时再重试（与 WMCL 行为一致）。
                if (AstralCode.TryParse(Input, out var raw, out _) && raw is not null &&
                    raw.ShortCode is { Length: > 0 })
                {
                    RememberRooms(AstralProfileStore.Remember(raw));
                    Input = "";
                    Preview = "";
                    Status = $"已添加短码房间 {raw.ShortCode}；短码暂未解析成功，加入时会重试。";
                    return;
                }

                Status = resolution.Error ?? "无法识别该邀请。";
                return;
            }

            var room = resolution.Room;
            if (room.ProviderId != "astral")
            {
                Status = "这个编号属于联机大厅，不能加到 Astral 房间列表。";
                return;
            }

            RememberRooms(AstralProfileStore.Remember(new AstralRoom(
                room.DisplayName, room.NetworkName, room.NetworkSecret, room.Peers, "Astral")
            {
                ShortCode = room.Code,
                IsProtected = room.IsProtected
            }));

            Input = "";
            Preview = "";
            Status = $"已添加到房间列表：{room.DisplayName}";
        }
        catch (Exception exception)
        {
            Status = "添加失败：" + exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    [RelayCommand]
    private async Task JoinSelectedAsync()
    {
        if (SelectedRoom is not { } entry || IsBusy) return;
        Input = entry.ShortCode is { Length: > 0 } code
            ? AstralCode.ShortLink(code)
            : AstralCode.EncodeRoomLink(entry.Name, entry.NetworkName, entry.NetworkSecret, entry.Peers);
        await JoinAsync();
    }

    [RelayCommand]
    private void RemoveRoom()
    {
        if (SelectedRoom is not { } entry) return;
        RememberRooms(AstralProfileStore.Remove(entry));
        SelectedRoom = null;
        Status = "已从房间列表移除：" + entry.Name;
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        if (!CanLeave) return;
        IsBusy = true;
        try
        {
            StopMemberPolling();
            await LobbyService.LeaveAllTransportsAsync();
            await _host.StopAsync();
            _joined = false;
            _joinedNetwork = "";
            RoomSummary = "";
            ShareShortLink = "";
            ShareLongLink = "";
            ConnectAddress = "";
            Status = "已退出。";
            HostStatus = "";
        }
        catch (Exception exception)
        {
            Status = "退出失败：" + exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    [RelayCommand]
    private async Task HostAsync()
    {
        if (!CanHost) return;
        IsBusy = true;
        HostStatus = "正在开房…";
        try
        {
            var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var peers = RelayChoices.Where(choice => choice.IsSelected).Select(choice => choice.Uri).ToList();
            if (peers.Count == 0) peers = [.. AstralCode.DefaultPeers()];

            var room = new AstralRoom(
                string.IsNullOrWhiteSpace(NewRoomName) ? "我的房间 " + suffix : NewRoomName.Trim(),
                IsProtectedRoom || string.IsNullOrWhiteSpace(NewRoomNetwork)
                    ? "astral-" + suffix.ToLowerInvariant()
                    : NewRoomNetwork.Trim(),
                IsProtectedRoom ? Guid.NewGuid().ToString("N")[..16] : NewRoomSecret.Trim(),
                peers,
                "Astral")
            {
                IsProtected = IsProtectedRoom
            };

            var result = await _host.StartAsync(room, EffectiveUserName);
            if (!result.Success)
            {
                HostStatus = result.Error ?? "开房失败。";
                return;
            }

            ApplyJoined(room.Name, room.NetworkName, result.ShortCode, 25565);
            ConnectAddress = AstralLobbyProvider.HostVirtualIp + ":25565";
            ShareShortLink = result.ShortLink ?? "";
            ShareLongLink = result.LongLink ?? "";

            RememberRooms(AstralProfileStore.Remember(room));
            StartMemberPolling();
            HostStatus = string.IsNullOrWhiteSpace(result.ShortCode)
                ? "房间已开好；短码服务不可用，请把长链发给朋友。"
                : "房间已开好，短码与长链都可以发给朋友。";
        }
        catch (Exception exception)
        {
            LogWrapper.Error(exception, "Astral", "开房异常");
            HostStatus = "开房失败：" + exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    private void RefreshRelayChoices()
    {
        RelayChoices.Clear();
        foreach (var relay in AstralProfileStore.LoadRelays())
            RelayChoices.Add(new AstralRelayChoice(relay.Uri, relay.Name));
        if (RelayChoices.Count == 0)
            foreach (var uri in AstralCode.DefaultPeers())
                RelayChoices.Add(new AstralRelayChoice(uri));
    }

    private void ApplyJoined(string name, string network, string? shortCode, int port)
    {
        _joined = true;
        _joinedNetwork = network;
        RoomSummary = $"房间：{name}　网络：{network}" +
                      (string.IsNullOrWhiteSpace(shortCode) ? "" : $"　短码：{shortCode}");
        ShareShortLink = string.IsNullOrWhiteSpace(shortCode)
            ? ""
            : AstralCode.ShortLink(shortCode!);
    }

    /// <summary>每 3 秒读一次 peer 表，更新成员列表。</summary>
    private void StartMemberPolling()
    {
        StopMemberPolling();
        _memberCts = new CancellationTokenSource();
        var token = _memberCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _joined)
            {
                var table = await LobbyConnector.QueryPeersAsync("astral", token).ConfigureAwait(false);
                var members = AstralMember.ParseTable(table);
                if (members.Count > 0)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Members.Clear();
                        foreach (var member in members) Members.Add(member);
                        OnPropertyChanged(nameof(HasMembers));
                    });
                }

                try
                {
                    await Task.Delay(3000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }, token);
    }

    private void StopMemberPolling()
    {
        _memberCts?.Cancel();
        _memberCts?.Dispose();
        _memberCts = null;
        Members.Clear();
        OnPropertyChanged(nameof(HasMembers));
    }

    private void RememberRooms(IReadOnlyList<AstralRoomEntry> rooms)
    {
        Rooms.Clear();
        foreach (var entry in rooms) Rooms.Add(entry);
        OnPropertyChanged(nameof(HasRooms));
    }

    private void NotifyCommands()
    {
        JoinCommand.NotifyCanExecuteChanged();
        JoinSelectedCommand.NotifyCanExecuteChanged();
        LeaveCommand.NotifyCanExecuteChanged();
        HostCommand.NotifyCanExecuteChanged();
    }
}
