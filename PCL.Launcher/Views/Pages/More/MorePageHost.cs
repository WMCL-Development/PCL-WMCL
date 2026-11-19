using Avalonia.Controls;
using PCL.Core.App.Localization;
using PCL.Navigation;

namespace PCL.Views.Pages.More;

/// <summary>更多页宿主：左栏常驻，右栏按子页描述缓存。</summary>
public sealed class MorePageHost : IPageHost
{
    private readonly PageDescriptor _descriptor;
    private readonly PageMoreLeft _left;
    private readonly PageViewFactory _pageViews;
    private readonly Dictionary<SubPageId, Control> _rightPages = new();

    public MorePageHost(
        PageDescriptor descriptor,
        PageMoreLeft left,
        INavigationService navigation,
        PageViewFactory pageViews)
    {
        _descriptor = descriptor;
        _pageViews = pageViews;
        _left = left;
        _left.PageSelected += sub =>
            navigation.GoTo(new PageLocation(PageIds.More, sub));
    }

    public PageId Page => PageIds.More;

    public MainTab? Tab => MainTab.More;

    public double LeftWidth => _descriptor.LeftWidth;

    public string GetTitle(PageRoute stack)
    {
        var sub = _descriptor.NormalizeSubPage(stack.SubPage);
        var descriptor = _descriptor.FindSubPage(sub);
        return descriptor is null ? string.Empty : Lang.Text(descriptor.TitleKey);
    }

    public Control? GetLeft(PageRoute stack)
    {
        var sub = _descriptor.NormalizeSubPage(stack.SubPage);
        _left.Select(sub);
        return _left;
    }

    public Control GetRight(PageRoute stack)
    {
        var sub = _descriptor.NormalizeSubPage(stack.SubPage);
        if (_rightPages.TryGetValue(sub, out var cached))
            return cached;

        var descriptor = _descriptor.FindSubPage(sub)
            ?? throw new InvalidOperationException($"未找到更多子页：{sub}");
        var page = _pageViews.Create(descriptor, stack);
        _rightPages[sub] = page;
        return page;
    }
}
