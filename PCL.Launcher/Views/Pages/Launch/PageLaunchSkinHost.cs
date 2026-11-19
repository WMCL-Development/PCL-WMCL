using Avalonia.Controls;
using PCL.Core.App.Localization;
using PCL.Navigation;

namespace PCL.Views.Pages.Launch;

/// <summary>皮肤管理页宿主：二级页面，无左栏。</summary>
public sealed class PageLaunchSkinHost : IPageHost
{
    private PageLaunchSkin? _right;

    public PageId Page => PageIds.LaunchSkin;

    public MainTab? Tab => null;

    public double LeftWidth => 0d;

    public string GetTitle(PageRoute stack) => Lang.Text("Launch.Skin.Title");

    public Control? GetLeft(PageRoute stack) => null;

    public Control GetRight(PageRoute stack) => _right ??= new PageLaunchSkin();
}
