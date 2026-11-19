using Avalonia.Controls;
using PCL.Core.App.Localization;
using PCL.Navigation;

namespace PCL.Views.Pages.Launch;

/// <summary>
///     皮肤编辑器页宿主：参数为皮肤文件路径，切换参数时重建页面。
/// </summary>
public sealed class PageLaunchSkinEditorHost : IPageHost
{
    private PageLaunchSkinEditor? _right;
    private object? _parameter;

    public PageType Page => PageType.LaunchSkinEditor;

    public MainTab? Tab => null;

    public double LeftWidth => 0d;

    public string GetTitle(PageStackData stack) => Lang.Text("Skin.Editor.Title");

    public Control? GetLeft(PageStackData stack) => null;

    public Control GetRight(PageStackData stack)
    {
        if (_right is not null && Equals(_parameter, stack.Parameter))
            return _right;

        var page = new PageLaunchSkinEditor();
        if (stack.Parameter is string path)
            page.LoadSkin(path);

        _right = page;
        _parameter = stack.Parameter;
        return page;
    }
}
