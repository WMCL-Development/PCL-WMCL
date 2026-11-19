using CommunityToolkit.Mvvm.ComponentModel;
using PCL.Core.App;
using PCL.Core.App.Essentials;
using PCL.Core.App.Localization;
using PCL.Core.Platform;
using PCL.Core.UI.Hints;
using PCL.Core.ViewModel;

namespace PCL;

/// <summary>设置页面中「启动器杂项」页的视图模型。</summary>
public partial class PageSetupLauncherMiscViewModel : ViewModelBase
{
    [ObservableProperty] private int _activityIndex = Math.Clamp(
        States.System.AnnounceSolution,
        0,
        2);

    [ObservableProperty]
    private string _httpProxyPassword = Config.Network.HttpProxy.CustomPassword;

    [ObservableProperty] private string _httpProxyText = Config.Network.HttpProxy.CustomAddress;

    [ObservableProperty]
    private string _httpProxyUsername = Config.Network.HttpProxy.CustomUsername;

    [ObservableProperty] private bool _telemetryEnabled = !TelemetryService.IsBuildUntrusted;

    public bool IsBuildUntrusted => TelemetryService.IsBuildUntrusted;

    public string LinuxProtocolAutoText =>
        Lang.Text(
            "Setup.Misc.Debug.LinuxProtocol.Auto",
            LinuxProtocolName);

    public string LinuxProtocolAutoToolTip =>
        Lang.Text(
            "Setup.Misc.Debug.LinuxProtocol.Auto.ToolTip",
            LinuxProtocolName);

    private static string LinuxProtocolName =>
        LinuxDisplayProtocolResolver.ResolveAuto() == LinuxDisplayProtocol.Wayland
            ? "Wayland"
            : "X11";

    public void Reload()
    {
        ActivityIndex = Math.Clamp(States.System.AnnounceSolution, 0, 2);
        HttpProxyText = Config.Network.HttpProxy.CustomAddress;
        HttpProxyUsername = Config.Network.HttpProxy.CustomUsername;
        HttpProxyPassword = Config.Network.HttpProxy.CustomPassword;
        TelemetryEnabled = !TelemetryService.IsBuildUntrusted;
    }

    /// <summary>Niri 配置文件路径（仅 Linux 有意义）。</summary>
    public string NiriConfigPath { get; } = PCL.Core.Platform.NiriWindowRuleService.GetConfigPath();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyNiriRule))]
    private bool _niriBusy;

    [ObservableProperty]
    private string _niriStatus = "";

    public bool CanApplyNiriRule => !NiriBusy && OperatingSystem.IsLinux();

    /// <summary>把 PCL CE 的窗口规则写进 niri 配置（保留用户其它内容，自动备份）。</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ApplyNiriRuleAsync()
    {
        if (!CanApplyNiriRule) return;
        NiriBusy = true;
        NiriStatus = Lang.Text("Setup.Misc.Niri.Working");
        try
        {
            var service = new PCL.Core.Platform.NiriWindowRuleService();
            var backup = await service.ApplyAsync(NiriConfigPath, true);
            NiriStatus = Lang.Text("Setup.Misc.Niri.Done", backup);
            HintService.Show(Hints.Success(Lang.Text("Setup.Misc.Niri.DoneShort")));
        }
        catch (Exception exception)
        {
            NiriStatus = Lang.Text("Setup.Misc.Niri.Failed", exception.Message);
            LogWrapper.Warn("UI", "写入 Niri 规则失败：" + exception.Message);
        }
        finally
        {
            NiriBusy = false;
        }
    }

    public void ApplyHttpProxy()
    {
        Config.Network.HttpProxy.CustomAddress = HttpProxyText ?? string.Empty;
        Config.Network.HttpProxy.CustomUsername = HttpProxyUsername ?? string.Empty;
        Config.Network.HttpProxy.CustomPassword = HttpProxyPassword ?? string.Empty;
        HintService.Show(Hints.Success(Lang.Text("Setup.Misc.Network.Proxy.Apply")));
    }
}