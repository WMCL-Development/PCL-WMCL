using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Core.App.Localization;
using PCL.Core.Link.Astral;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Logging;

namespace PCL;

/// <summary>联机页的 EasyTier 直连与编号识别：不经过大厅或 Astral 也能组网。</summary>
public partial class PageToolsGameLinkViewModel
{
    private EasyTierEntity? _directEntity;

    [ObservableProperty] public partial string DirectNetworkName { get; set; } = string.Empty;

    [ObservableProperty] public partial string DirectNetworkSecret { get; set; } = string.Empty;

    [ObservableProperty] public partial string DirectPeers { get; set; } = string.Empty;

    [ObservableProperty] public partial string DirectStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartDirect))]
    [NotifyCanExecuteChangedFor(nameof(StartDirectCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopDirectCommand))]
    public partial bool DirectRunning { get; set; }

    public bool CanStartDirect => !DirectRunning
        && !string.IsNullOrWhiteSpace(DirectNetworkName)
        && !string.IsNullOrWhiteSpace(DirectNetworkSecret);

    partial void OnDirectNetworkNameChanged(string value) => OnPropertyChanged(nameof(CanStartDirect));

    partial void OnDirectNetworkSecretChanged(string value) => OnPropertyChanged(nameof(CanStartDirect));

    /// <summary>粘入编号后识别类型，供界面提示是陶瓦房间还是 PCL CE 大厅。</summary>
    public string JoinCodeKindText
    {
        get
        {
            var code = JoinCode?.Trim();
            if (string.IsNullOrWhiteSpace(code) || code.Length < 9) return string.Empty;
            var info = LobbyInfoProvider.ParseCode(code);
            if (info is null) return Lang.Text("Tools.GameLink.Code.Unknown");
            return info.Type == LobbyInfoProvider.LobbyType.Terracotta
                ? Lang.Text("Tools.GameLink.Code.Terracotta")
                : Lang.Text("Tools.GameLink.Code.Lobby");
        }
    }

    partial void OnJoinCodeChanged(string value) => OnPropertyChanged(nameof(JoinCodeKindText));

    [RelayCommand(CanExecute = nameof(CanStartDirect))]
    private void StartDirect()
    {
        if (!CanStartDirect) return;
        try
        {
            var peers = string.IsNullOrWhiteSpace(DirectPeers)
                ? AstralCode.DefaultPeers().ToList()
                : DirectPeers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            _directEntity = new EasyTierEntity(new EasyTierOptions
            {
                NetworkName = DirectNetworkName.Trim(),
                NetworkSecret = DirectNetworkSecret.Trim(),
                Peers = peers,
                DisableHolePunching = EasyTierDefaults.DisableHolePunching,
                DisableIpv6 = EasyTierDefaults.DisableIpv6,
                LatencyFirst = EasyTierDefaults.LatencyFirst,
                MachineId = Environment.MachineName
            });

            var port = _directEntity.Launch();
            DirectRunning = true;
            DirectStatus = Lang.Text("Tools.GameLink.Direct.Running", port);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Link", "EasyTier 直连启动失败：" + exception.Message);
            DirectStatus = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(DirectRunning))]
    private async Task StopDirectAsync()
    {
        if (_directEntity is null) return;
        try
        {
            await _directEntity.StopAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Link", "EasyTier 直连停止失败：" + exception.Message);
        }
        finally
        {
            _directEntity = null;
            DirectRunning = false;
            DirectStatus = string.Empty;
        }
    }
}
