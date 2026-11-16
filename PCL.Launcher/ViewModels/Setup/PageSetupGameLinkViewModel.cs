using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.Localization;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.UI.Hints;
using PCL.Core.ViewModel;

namespace PCL;

/// <summary>设置页面中「联机」页的视图模型。</summary>
public partial class PageSetupGameLinkViewModel : ViewModelBase
{

    public PageSetupGameLinkViewModel()
    {
        LoadAstralRelays();
    }

    [ObservableProperty] private bool _enableCliOutput = Config.Link.EnableCliOutput;

    [ObservableProperty] private bool _enableIPv6 = Config.Link.EnableIPv6;

    [ObservableProperty] private string _ipv6StatusText = Lang.Text(
        "Setup.GameLink.NetworkTest.Ipv6Status",
        Lang.Text("Setup.GameLink.NetworkTest.NotTested"));

    private CliNetTest.NetStatus? _lastStatus;

    [ObservableProperty] private bool _latencyFirstMode = Config.Link.UseLatencyFirstMode;

    [ObservableProperty] private string _linkUsername = Config.Link.Username;

    /// <summary>0 大厅 / 1 Astral / 2 陶瓦 / 3 EasyTier 直连，写入 <c>Config.Link.ConnectionMethod</c>。</summary>
    [ObservableProperty] private int _connectionMethodIndex = ConnectionMethodToIndex(Config.Link.ConnectionMethod);

    private static readonly string[] _connectionMethods = ["lobby", "astral", "terracotta", "easytier"];

    private static int ConnectionMethodToIndex(string? method) =>
        Math.Max(0, Array.FindIndex(_connectionMethods,
            item => string.Equals(item, method, StringComparison.OrdinalIgnoreCase)));

    partial void OnConnectionMethodIndexChanged(int value)
    {
        if (value < 0 || value >= _connectionMethods.Length) return;
        Config.Link.ConnectionMethod = _connectionMethods[value];
    }

    [ObservableProperty] private string _astralUsername = Config.Link.AstralUsername;

    [ObservableProperty] private string _astralVirtualIp = Config.Link.AstralVirtualIp;

    /// <summary>Astral 自定义中继节点。</summary>
    public System.Collections.ObjectModel.ObservableCollection<PCL.Core.Link.Astral.AstralRelay> AstralRelays { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemoveRelay))]
    private PCL.Core.Link.Astral.AstralRelay? _selectedAstralRelay;

    [ObservableProperty] private string _newAstralRelayUri = "";

    [ObservableProperty] private string _newAstralRelayName = "";

    public bool CanRemoveRelay => SelectedAstralRelay is not null;

    public bool HasAstralRelays => AstralRelays.Count > 0;

    partial void OnAstralUsernameChanged(string value) => Config.Link.AstralUsername = value ?? "";

    partial void OnAstralVirtualIpChanged(string value) => Config.Link.AstralVirtualIp = value ?? "";

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void AddAstralRelay()
    {
        var uri = NewAstralRelayUri.Trim();
        if (!PCL.Core.Link.Astral.AstralRelay.IsValidUri(uri))
        {
            HintService.Show(Hints.Warning(Lang.Text("Setup.GameLink.Astral.Relay.Invalid")));
            return;
        }

        if (AstralRelays.Any(item => string.Equals(item.Uri, uri, StringComparison.OrdinalIgnoreCase)))
        {
            HintService.Show(Hints.Warning(Lang.Text("Setup.GameLink.Astral.Relay.Exists")));
            return;
        }

        AstralRelays.Add(new PCL.Core.Link.Astral.AstralRelay(uri, NewAstralRelayName.Trim()));
        PCL.Core.Link.Astral.AstralProfileStore.SaveRelays([.. AstralRelays]);
        NewAstralRelayUri = "";
        NewAstralRelayName = "";
        OnPropertyChanged(nameof(HasAstralRelays));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void RemoveAstralRelay()
    {
        if (SelectedAstralRelay is not { } relay) return;
        AstralRelays.Remove(relay);
        PCL.Core.Link.Astral.AstralProfileStore.SaveRelays([.. AstralRelays]);
        SelectedAstralRelay = null;
        OnPropertyChanged(nameof(HasAstralRelays));
    }

    private void LoadAstralRelays()
    {
        AstralRelays.Clear();
        foreach (var relay in PCL.Core.Link.Astral.AstralProfileStore.LoadRelays())
            AstralRelays.Add(relay);
        OnPropertyChanged(nameof(HasAstralRelays));
    }

    [ObservableProperty] private string _netTestButtonText =
        Lang.Text("Setup.GameLink.NetworkTest.Start");

    [ObservableProperty] private bool _netTestEnabled = true;

    [ObservableProperty] private int _preferProtocolIndex = (int)Config.Link.ProtocolPreference;

    [ObservableProperty] private string _tcpNatTypeText = Lang.Text(
        "Setup.GameLink.NetworkTest.TcpNatType",
        Lang.Text("Setup.GameLink.NetworkTest.NotTested"));

    [ObservableProperty] private bool _tryPunchSym = Config.Link.TryPunchSym;

    [ObservableProperty] private string _udpNatTypeText = Lang.Text(
        "Setup.GameLink.NetworkTest.UdpNatType",
        Lang.Text("Setup.GameLink.NetworkTest.NotTested"));

    partial void OnLinkUsernameChanged(string value)
    {
        Config.Link.Username = value ?? string.Empty;
    }

    partial void OnLatencyFirstModeChanged(bool value)
    {
        Config.Link.UseLatencyFirstMode = value;
    }

    partial void OnTryPunchSymChanged(bool value)
    {
        Config.Link.TryPunchSym = value;
    }

    partial void OnEnableIPv6Changed(bool value)
    {
        Config.Link.EnableIPv6 = value;
    }

    partial void OnEnableCliOutputChanged(bool value)
    {
        Config.Link.EnableCliOutput = value;
    }

    partial void OnPreferProtocolIndexChanged(int value)
    {
        if (value >= 0) SetProtocolPreference(value);
    }

    public void RefreshLocalization()
    {
        NetTestButtonText = Lang.Text(
            NetTestEnabled
                ? "Setup.GameLink.NetworkTest.Start"
                : "Setup.GameLink.NetworkTest.Testing");
        if (_lastStatus is null)
        {
            UdpNatTypeText = Lang.Text(
                "Setup.GameLink.NetworkTest.UdpNatType",
                Lang.Text("Setup.GameLink.NetworkTest.NotTested"));
            TcpNatTypeText = Lang.Text(
                "Setup.GameLink.NetworkTest.TcpNatType",
                Lang.Text("Setup.GameLink.NetworkTest.NotTested"));
            Ipv6StatusText = Lang.Text(
                "Setup.GameLink.NetworkTest.Ipv6Status",
                Lang.Text("Setup.GameLink.NetworkTest.NotTested"));
            return;
        }

        ApplyStatus(_lastStatus);
    }

    private void ApplyStatus(CliNetTest.NetStatus status)
    {
        UdpNatTypeText = Lang.Text(
            "Setup.GameLink.NetworkTest.UdpNatType",
            CliNetTest.GetNatTypeString(status.UdpNatType));
        TcpNatTypeText = Lang.Text(
            "Setup.GameLink.NetworkTest.TcpNatType",
            CliNetTest.GetNatTypeString(status.TcpNatType));
        Ipv6StatusText = Lang.Text(
            "Setup.GameLink.NetworkTest.Ipv6Status",
            status.SupportIPv6
                ? Lang.Text("Setup.GameLink.NetworkTest.Supported")
                : Lang.Text("Setup.GameLink.NetworkTest.Unsupported"));
    }

    public void Reload()
    {
        LinkUsername = Config.Link.Username;
        LatencyFirstMode = Config.Link.UseLatencyFirstMode;
        PreferProtocolIndex = Math.Clamp((int)Config.Link.ProtocolPreference, 0, 1);
        TryPunchSym = Config.Link.TryPunchSym;
        EnableIPv6 = Config.Link.EnableIPv6;
        EnableCliOutput = Config.Link.EnableCliOutput;
    }

    public void Reset()
    {
        try
        {
            Config.Link.Reset();
            HintService.Show(Hints.Success(Lang.Text("Setup.GameLink.Initialized"), false));
            Reload();
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(
                ex,
                Lang.Text("Setup.GameLink.Error.InitFailed"),
                LauncherLogLevel.Msgbox,
                userSummary: Lang.Text("Setup.GameLink.Error.InitFailed"));
        }

        Reload();
    }

    public void SetConfigByTag(string tag, object value)
    {
        ConfigService.TrySetValue(tag, value);
    }

    public void SetProtocolPreference(int index)
    {
        try
        {
            Config.Link.ProtocolPreference = (LinkProtocolPreference)index;
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(
                ex,
                Lang.Text("Setup.GameLink.Error.ConfigChangeFailed"),
                LauncherLogLevel.Hint,
                userSummary: Lang.Text("Setup.GameLink.Error.ConfigChangeFailed"));
        }
    }

    public async Task RunNetworkTestAsync()
    {
        NetTestEnabled = false;
        NetTestButtonText = Lang.Text("Setup.GameLink.NetworkTest.Testing");
        try
        {
            var status = await RunAsync(ct => Task.Run(() => CliNetTest.GetNetStatusAsync(ct), ct));
            if (status is null)
                return;
            _lastStatus = status;
            ApplyStatus(status);
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(
                ex,
                "[Link] 获取网络测试结果失败",
                LauncherLogLevel.Hint,
                userSummary: Lang.Text("Setup.GameLink.Error.NetworkTestFailed"));
        }
        finally
        {
            NetTestEnabled = true;
            NetTestButtonText = Lang.Text("Setup.GameLink.NetworkTest.Start");
        }
    }
}