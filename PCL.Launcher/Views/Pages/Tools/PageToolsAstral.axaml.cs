using System.Threading.Tasks;
using Avalonia.Controls;
using PCL.UI.Controls;
using Avalonia.Input.Platform;
using PCL.Core.App.Localization;
using PCL.Core.UI.Hints;

namespace PCL.Views.Pages.Tools;

/// <summary>
///     Astral 联机面板：嵌在联机大厅页里，用顶部按钮切换显示。
///     联机逻辑在 PCL.Core，这里只做绑定与剪贴板。
/// </summary>
public partial class PageToolsAstral : MyPageRight
{
    public PageToolsAstral()
    {
        InitializeComponent();
        PanScroll = PanBack;
        DataContext = ViewModel;

        BtnCopyShort.Click += (_, _) => _ = CopyAsync(ViewModel.ShareShortLink, "短链");
        BtnCopyLong.Click += (_, _) => _ = CopyAsync(ViewModel.ShareLongLink, "长链");
        BtnCopyIp.Click += (_, _) => _ = CopyAsync(ViewModel.ConnectAddress, "连接地址");
    }

    public PageToolsAstralViewModel ViewModel { get; } = new();

    private async Task CopyAsync(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            HintService.Show(Hints.Warning($"{label}还没生成。"));
            return;
        }

        if (PCL.UI.ClipboardHelper.Service is not { } clipboard)
        {
            HintService.Show(Hints.Warning("没有可用的剪贴板。"));
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            HintService.Show(Hints.Success($"已复制{label}。"));
        }
        catch (System.Exception exception)
        {
            HintService.Show(Hints.Warning($"复制失败：{exception.Message}"));
        }
    }
}
