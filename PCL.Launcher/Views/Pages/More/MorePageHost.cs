using Avalonia.Controls;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Navigation;
using PCL.Views.Pages.Setup;
using PCL.Views.Pages.Tools;

namespace PCL.Views.Pages.More;

/// <summary>
///     更多页宿主：左栏常驻，右栏按 <see cref="PageSubType" /> 缓存
///     百宝箱 / 关于 / 反馈三个子页；帮助是外部链接。
/// </summary>
public sealed class MorePageHost : IPageHost
{
    private readonly MorePageLeft _left;
    private readonly Dictionary<PageSubType, Control> _rightPages = new();

    public MorePageHost()
    {
        _left = new MorePageLeft();
        _left.PageSelected += sub => PageRouter.Navigate(PageType.More, sub);
    }

    public PageType Page => PageType.More;

    public MainTab? Tab => MainTab.More;

    public double LeftWidth => 150d;

    public string GetTitle(PageStackData stack) => Normalize(stack.SubType) switch
    {
        PageSubType.MoreAbout => Lang.Text("More.Left.About"),
        PageSubType.MoreFeedback => Lang.Text("More.Left.Feedback"),
        PageSubType.MoreHelp => Lang.Text("More.Left.Help"),
        _ => Lang.Text("More.Left.Toolbox")
    };

    public Control? GetLeft(PageStackData stack)
    {
        _left.Select(Normalize(stack.SubType));
        return _left;
    }

    public Control GetRight(PageStackData stack)
    {
        var sub = Normalize(stack.SubType);
        if (_rightPages.TryGetValue(sub, out var cached)) return cached;

        Control page = sub switch
        {
            PageSubType.MoreAbout => new PageSetupAbout(),
            PageSubType.MoreFeedback => new PageSetupFeedback(),
            PageSubType.MoreHelp => new PageMoreHelp(),
            _ => new PageToolsTest()
        };
        _rightPages[sub] = page;
        return page;
    }

    private static PageSubType Normalize(PageSubType sub) =>
        sub is PageSubType.MoreToolbox or PageSubType.MoreAbout or PageSubType.MoreFeedback
            or PageSubType.MoreHelp
            ? sub
            : PageSubType.MoreToolbox;

}
