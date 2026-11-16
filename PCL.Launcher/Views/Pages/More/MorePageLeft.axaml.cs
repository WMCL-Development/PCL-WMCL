using Avalonia.Controls;
using PCL.Navigation;
using PCL.UI.Controls;
using PCL.Views.Shell;

namespace PCL.Views.Pages.More;

/// <summary>更多页左栏：百宝箱 / 帮助 / 关于 / 反馈。帮助是外链入口，不参与路由。</summary>
public partial class MorePageLeft : MyPageLeft
{
    private readonly List<(PageSubType Sub, MyListItem Item)> _items = [];
    private bool _isSynchronizing;

    public MorePageLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;

        _items.Add((PageSubType.MoreToolbox, ItemToolbox));
        _items.Add((PageSubType.MoreAbout, ItemAbout));
        _items.Add((PageSubType.MoreFeedback, ItemFeedback));
        _items.Add((PageSubType.MoreHelp, ItemHelp));

        foreach (var (sub, item) in _items)
            item.Check += (_, _) => OnItemChecked(sub, item);


    }

    public event Action<PageSubType>? PageSelected;

    /// <summary>帮助是外部链接，交给宿主处理。</summary>
    public event Action? HelpRequested;

    public void Select(PageSubType sub)
    {
        _isSynchronizing = true;
        try
        {
            foreach (var (itemSub, item) in _items)
                item.SetChecked(itemSub == sub, false, false);
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void OnItemChecked(PageSubType sub, MyListItem item)
    {
        if (_isSynchronizing || !item.Checked) return;
        PageSelected?.Invoke(sub);
    }
}
