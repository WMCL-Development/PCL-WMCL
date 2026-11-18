using Avalonia.Controls;
using PCL.Core.App.Localization;
using PCL.Navigation;

namespace PCL.Views.Pages.Launch;

/// <summary>
///     皮肤管理页宿主：左栏沿用账号面板，右栏为皮肤管理内容。
///     作为独立二级页注册，标题栏会自动显示内嵌标题与返回箭头，与其他二级页一致。
/// </summary>
public sealed class PageLaunchSkinHost : IPageHost
{
    private PageLaunchSkin? _right;

    public PageType Page => PageType.LaunchSkin;

    public MainTab? Tab => null;

    public double LeftWidth => 0d;

    public string GetTitle(PageStackData stack) => Lang.Text("Launch.Skin.Title");

    public Control? GetLeft(PageStackData stack) => null;

    public Control GetRight(PageStackData stack) => _right ??= new PageLaunchSkin();
}
