using Avalonia.Controls;
using PCL.Core.App.Localization;
using PCL.Navigation;
using PCL.Views.Pages.Tools;

namespace PCL.Views.Pages.Link;

/// <summary>
///     联机顶级页：单页无左栏，内部用顶部按钮在「联机大厅」与「Astral 联机」之间切换。
/// </summary>
public sealed class LinkPageHost : IPageHost
{
    private readonly PageToolsGameLink _page = new();

    public PageType Page => PageType.Link;

    public MainTab? Tab => MainTab.Link;

    public double LeftWidth => 0d;

    public string GetTitle(PageStackData stack) => Lang.Text("Main.Tab.Link");

    public Control? GetLeft(PageStackData stack) => null;

    public Control GetRight(PageStackData stack) => _page;
}
