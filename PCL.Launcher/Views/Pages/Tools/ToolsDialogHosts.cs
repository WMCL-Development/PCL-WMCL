using Avalonia.Controls;
using PCL.Core.App.Localization;
using PCL.Navigation;

namespace PCL.Views.Pages.Tools;

/// <summary>收藏库宿主：二级页，无左栏。</summary>
public sealed class CollectionHost : IPageHost
{
    private PageCollection? _right;

    public PageId Page => PageIds.Collection;

    public MainTab? Tab => null;

    public double LeftWidth => 0d;

    public string GetTitle(PageRoute stack) => Lang.Text("Tools.Test.Lottery.CollectionHeader");

    public Control? GetLeft(PageRoute stack) => null;

    public Control GetRight(PageRoute stack) => _right ??= new PageCollection();
}

/// <summary>选择物品宿主：二级页，无左栏。</summary>
public sealed class ItemPickerHost : IPageHost
{
    private PageItemPicker? _right;

    public PageId Page => PageIds.ItemPicker;

    public MainTab? Tab => null;

    public double LeftWidth => 0d;

    public string GetTitle(PageRoute stack) => Lang.Text("Tools.Test.Achievement.Pick");

    public Control? GetLeft(PageRoute stack) => null;

    public Control GetRight(PageRoute stack) => _right ??= new PageItemPicker();
}
