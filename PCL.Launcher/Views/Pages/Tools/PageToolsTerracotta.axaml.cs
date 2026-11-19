using Avalonia.Controls;
using PCL.Core.App.Localization;
using PCL.Core.Platform;
using PCL.Core.Logging;
using PCL.Core.UI.Hints;
using PCL.ViewModels.Pages;

namespace PCL.Views.Pages.Tools;

/// <summary>陶瓦联机面板：只认陶瓦编号，加入时不校验账号。</summary>
public partial class PageToolsTerracotta : UserControl
{
    public PageToolsTerracotta()
    {
        InitializeComponent();
        DataContext = new PageToolsTerracottaViewModel();
        PanHostCode.PointerReleased += (_, _) => CopyHostCodeAsync();
    }

    /// <summary>点击房间编号：复制到剪贴板并提示。</summary>
    private void CopyHostCodeAsync()
    {
        if (DataContext is not PageToolsTerracottaViewModel viewModel) return;

        var code = viewModel.HostCode;
        if (string.IsNullOrWhiteSpace(code)) return;

        try
        {
            PlatformServices.Clipboard?.SetText(code);
            HintService.Show(Hints.Success(Lang.Text("Common.Hint.Copied")));
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Link", "复制陶瓦房间编号失败：" + exception.Message);
        }
    }
}
