using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Core.App.Localization;
using PCL.Core.Link.Astral;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Logging;

namespace PCL;

/// <summary>联机页的 EasyTier 直连：自己约定网络名与密钥即可组网，不经过大厅或 Astral。</summary>
public partial class PageToolsDirectViewModel : PCL.Core.ViewModel.ViewModelBase
{
    private PCL.Core.Link.EasyTier.IEasyTierProcess? _directEntity;
    private CancellationTokenSource? _directPoll;

    /// <summary>成员列表：来自实例的 peer 表。</summary>
    public ObservableCollection<AstralMember> DirectMembers { get; } = [];

    public bool HasDirectMembers => DirectMembers.Count > 0;

    [ObservableProperty] public partial string DirectNetworkName { get; set; } = PCL.Core.App.Config.Link.DirectNetworkName;

    /// <summary>房主的虚拟 IP（无 tun 平台用端口转发时需要）。</summary>
    [ObservableProperty] public partial string DirectHostIp { get; set; } = PCL.Core.App.Config.Link.DirectHostIp;

    /// <summary>本地转发端口，Minecraft 连 127.0.0.1:该端口 即可。</summary>
    [ObservableProperty] public partial int DirectLocalPort { get; set; } = PCL.Core.App.Config.Link.DirectLocalPort;

    [ObservableProperty] public partial string DirectNetworkSecret { get; set; } = PCL.Core.App.Config.Link.DirectNetworkSecret;

    [ObservableProperty] public partial string DirectPeers { get; set; } = PCL.Core.App.Config.Link.DirectPeers;

    [ObservableProperty] public partial string DirectStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartDirect))]
    [NotifyCanExecuteChangedFor(nameof(StartDirectCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopDirectCommand))]
    public partial bool DirectRunning { get; set; }

    public bool CanStartDirect => !DirectRunning
        && !string.IsNullOrWhiteSpace(DirectNetworkName)
        && !string.IsNullOrWhiteSpace(DirectNetworkSecret);

    partial void OnDirectNetworkNameChanged(string value)
    {
        PCL.Core.App.Config.Link.DirectNetworkName = value;
        OnPropertyChanged(nameof(CanStartDirect));
    }

    partial void OnDirectNetworkSecretChanged(string value)
    {
        PCL.Core.App.Config.Link.DirectNetworkSecret = value;
        OnPropertyChanged(nameof(CanStartDirect));
    }

    partial void OnDirectPeersChanged(string value) => PCL.Core.App.Config.Link.DirectPeers = value;

    partial void OnDirectHostIpChanged(string value) => PCL.Core.App.Config.Link.DirectHostIp = value;

    partial void OnDirectLocalPortChanged(int value) => PCL.Core.App.Config.Link.DirectLocalPort = value;

    [RelayCommand(CanExecute = nameof(CanStartDirect))]
    private async Task StartDirectAsync()
    {
        if (!CanStartDirect) return;
        try
        {
            var peers = string.IsNullOrWhiteSpace(DirectPeers)
                ? AstralCode.DefaultPeers().ToList()
                : DirectPeers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            var isHost = string.IsNullOrWhiteSpace(DirectHostIp);
            var forwards = new List<string>();
            if (!string.IsNullOrWhiteSpace(DirectHostIp) && DirectLocalPort is > 0 and < 65536)
                forwards.Add($"tcp://127.0.0.1:{DirectLocalPort}/{DirectHostIp.Trim()}:25565");

            _directEntity = PCL.Core.Link.EasyTier.EasyTierProcessFactory.Create(new EasyTierOptions
            {
                PortForwards = forwards,
                AutoForwardLocalPort = forwards.Count == 0 ? DirectLocalPort : null,
                NetworkName = DirectNetworkName.Trim(),
                NetworkSecret = DirectNetworkSecret.Trim(),
                Peers = peers,
                DisableHolePunching = EasyTierDefaults.DisableHolePunching,
                DisableIpv6 = EasyTierDefaults.DisableIpv6,
                LatencyFirst = EasyTierDefaults.LatencyFirst,
                AsHost = isHost,
                NoTun = !isHost,
                VirtualIp = isHost ? "10.126.126.1" : null,
                Hostname = string.IsNullOrWhiteSpace(PCL.Core.App.Config.Link.Username)
                    ? Environment.MachineName
                    : PCL.Core.App.Config.Link.Username.Trim(),
                MachineId = Environment.MachineName
            });

            var code = await _directEntity.LaunchAsync().ConfigureAwait(true);
            DirectRunning = code == 0;
            DirectStatus = code == 0
                ? Lang.Text("Tools.GameLink.Direct.Running", forwards.Count > 0 ? DirectLocalPort : 0)
                : Lang.Text("Tools.GameLink.Direct.Failed");

            if (code == 0) StartDirectMemberPolling();
            else StopDirectMemberPolling();
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Link", "EasyTier 直连启动失败：" + exception.Message);
            DirectStatus = exception.Message;
        }
    }

    private void StartDirectMemberPolling()
    {
        StopDirectMemberPolling();
        _directPoll = new CancellationTokenSource();
        var token = _directPoll.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var table = _directEntity is null ? null : await _directEntity.QueryPeersAsync().ConfigureAwait(false);
                    var members = AstralMember.ParseTable(table);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        DirectMembers.Clear();
                        foreach (var member in members) DirectMembers.Add(member);
                        OnPropertyChanged(nameof(HasDirectMembers));
                        DirectStatus = Lang.Text("Tools.GameLink.Direct.Running", DirectLocalPort) + " · 成员 " + DirectMembers.Count;
                    });
                }
                catch (Exception exception)
                {
                    LogWrapper.Warn("Link", "读取直连成员失败：" + exception.Message);
                }

                try { await Task.Delay(3000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }, token);
    }

    private void StopDirectMemberPolling()
    {
        _directPoll?.Cancel();
        _directPoll?.Dispose();
        _directPoll = null;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DirectMembers.Clear();
            OnPropertyChanged(nameof(HasDirectMembers));
        });
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
            StopDirectMemberPolling();
        }
    }
}
