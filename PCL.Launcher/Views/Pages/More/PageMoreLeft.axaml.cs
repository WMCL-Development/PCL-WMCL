using PCL.Navigation;
using PCL.UI.Controls;
using PCL.UI.Controls.Navigation;
using PCL.Views.Shell;

namespace PCL.Views.Pages.More;

/// <summary>更多页左栏：由页面描述生成百宝箱 / 帮助 / 关于 / 反馈导航项。</summary>
public partial class PageMoreLeft : MyPageLeft
{
    private static readonly PageDescriptor Page = PageCatalog.RequirePage(PageIds.More);
    private readonly List<(SubPageId Sub, MyListItem Item)> _items = [];
    private bool _isSynchronizing;

    public PageMoreLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;
        BuildItems();
        AttachedToVisualTree += (_, _) =>
        {
            HiddenFeatureState.Changed += OnHiddenStateChanged;
            ApplyHiddenState();
        };
        DetachedFromVisualTree += (_, _) => HiddenFeatureState.Changed -= OnHiddenStateChanged;
    }

    public event Action<SubPageId>? PageSelected;

    public override void OnNavigatedTo(PageParameter? parameter)
    {
        ApplyHiddenState();
    }

    public override void OnNavigatedFrom()
    {
    }

    public void Select(SubPageId subPage)
    {
        var normalized = Page.NormalizeSubPage(subPage);
        _isSynchronizing = true;
        try
        {
            foreach (var (itemSub, item) in _items)
                item.SetChecked(itemSub == normalized, false, false);
        }
        finally
        {
            _isSynchronizing = false;
        }

        ApplyHiddenState();
    }

    private void OnHiddenStateChanged()
    {
        BuildItems();
        ApplyHiddenState();
    }

    public void ApplyHiddenState()
    {
        var visible = Page
            .VisibleSubPages(HiddenFeatureState.ForceShow)
            .Select(item => item.Id)
            .ToHashSet();
        foreach (var (sub, item) in _items)
            item.IsVisible = visible.Contains(sub);
    }

    private void BuildItems()
    {
        var result = PageNavigationListBuilder.Build(
            Page,
            PanItem,
            HiddenFeatureState.ForceShow,
            sub =>
            {
                if (_isSynchronizing) return;
                PageSelected?.Invoke(sub);
            });
        _items.Clear();
        _items.AddRange(result.Items);
    }
}
