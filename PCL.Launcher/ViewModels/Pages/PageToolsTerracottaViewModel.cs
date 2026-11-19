using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Core.App.Localization;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Link.Terracotta;
using PCL.Core.Logging;
using PCL.Core.ViewModel;

namespace PCL.ViewModels.Pages;

/// <summary>
///     陶瓦联机：加入走编号解码，创建走本地生成编号 + EasyTier 主机模式 + 端口桥接。
///     两种方式都不需要账号，也不依赖陶瓦客户端。
/// </summary>
public partial class PageToolsTerracottaViewModel : ViewModelBase
{
    partial void OnLocalPortChanged(int value) => PCL.Core.App.Config.Link.TerracottaLocalPort = value;
    private PCL.Core.Link.EasyTier.IEasyTierProcess? _entity;
    private TerracottaTunnel? _tunnel;

    [ObservableProperty] public partial string Code { get; set; } = string.Empty;

    [ObservableProperty] public partial string Preview { get; set; } = string.Empty;

    [ObservableProperty] public partial string Status { get; set; } = string.Empty;

    [ObservableProperty] public partial string HostCode { get; set; } = string.Empty;

    [ObservableProperty] public partial string HostStatus { get; set; } = string.Empty;

    [ObservableProperty] public partial int LocalPort { get; set; } = PCL.Core.App.Config.Link.TerracottaLocalPort;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    [NotifyCanExecuteChangedFor(nameof(LeaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(HostCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopHostCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(HostCommand))]
    public partial bool IsHosting { get; set; }

    partial void OnCodeChanged(string value)
    {
        Preview = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return;

        var info = LobbyInfoProvider.ParseCode(value.Trim());
        Preview = info is null
            ? Lang.Text("Tools.Terracotta.Preview.Invalid")
            : info.Type == LobbyInfoProvider.LobbyType.Terracotta
                ? Lang.Text("Tools.Terracotta.Preview.Ok", info.Port)
                : Lang.Text("Tools.Terracotta.Preview.NotTerracotta");
    }

    private bool CanJoin => !IsRunning && !string.IsNullOrWhiteSpace(Code);

    private bool CanHost => !IsHosting && LocalPort is > 0 and < 65536;

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private async Task JoinAsync()
    {
        var info = LobbyInfoProvider.ParseCode(Code.Trim());
        if (info is null || info.Type != LobbyInfoProvider.LobbyType.Terracotta)
        {
            Status = Lang.Text("Tools.Terracotta.Preview.Invalid");
            return;
        }

        Status = Lang.Text("Tools.Terracotta.Status.Joining");
        try
        {
            var username = LobbyInfoProvider.GetUsername();
            if (string.IsNullOrWhiteSpace(username)) username = Environment.UserName;

            var ok = await LobbyService.JoinLobbyAsync(Code.Trim(), username).ConfigureAwait(true);
            IsRunning = ok;
            Status = ok
                ? Lang.Text("Tools.Terracotta.Status.Joined", info.Port)
                : Lang.Text("Tools.Terracotta.Status.Failed");
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Link", "加入陶瓦房间失败：" + exception.Message);
            Status = exception.Message;
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task LeaveAsync()
    {
        try
        {
            await LobbyService.LeaveLobbyAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Link", "退出陶瓦房间失败：" + exception.Message);
        }
        finally
        {
            IsRunning = false;
            Status = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanHost))]
    private async Task HostAsync()
    {
        HostStatus = Lang.Text("Tools.Terracotta.Host.Starting");
        try
        {
            var code = TerracottaCode.Create(out var name, out var secret, out var port);

            _entity = PCL.Core.Link.EasyTier.EasyTierProcessFactory.Create(new EasyTierOptions
            {
                NetworkName = "terracotta-mc-" + name,
                NetworkSecret = secret,
                AsHost = true,
                VirtualIp = "10.144.144.1",
                MinecraftPort = LocalPort,
                NoTun = true,
                PrivateMode = true,
                MachineId = Environment.MachineName,
                DisableHolePunching = EasyTierDefaults.DisableHolePunching,
                DisableIpv6 = EasyTierDefaults.DisableIpv6,
                LatencyFirst = EasyTierDefaults.LatencyFirst
            });

            var launched = _entity.Launch();
            if (launched != 0)
            {
                HostStatus = Lang.Text("Tools.Terracotta.Host.Failed");
                return;
            }

            _tunnel = new TerracottaTunnel(port, LocalPort);
            _tunnel.Start();

            HostCode = code;
            IsHosting = true;
            HostStatus = Lang.Text("Tools.Terracotta.Host.Ready", port, LocalPort);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Link", "创建陶瓦房间失败：" + exception.Message);
            HostStatus = exception.Message;
            await StopHostInternalAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(IsHosting))]
    private async Task StopHostAsync() => await StopHostInternalAsync().ConfigureAwait(true);

    private async Task StopHostInternalAsync()
    {
        _tunnel?.Dispose();
        _tunnel = null;

        if (_entity is not null)
        {
            try
            {
                await _entity.StopAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                LogWrapper.Debug("Link", "停止陶瓦主机失败：" + exception.Message);
            }

            _entity = null;
        }

        IsHosting = false;
        HostCode = string.Empty;
        HostStatus = string.Empty;
    }
}
