using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using PCL.Core.App.Localization;
using PCL.UI.Controls;

namespace PCL.Views.Pages.Instance;

/// <summary>
///     资源更新审阅：列出所有可更新的资源，逐项勾选后只更新选中项。
/// </summary>
public partial class ResourceUpdateReviewPanel : UserControl
{
    private readonly List<(ResourceUpdateReviewItem Item, CheckBox Box)> _rows = [];

    public ResourceUpdateReviewPanel() : this([])
    {
    }

    public ResourceUpdateReviewPanel(IReadOnlyList<ResourceUpdateReviewItem> items)
    {
        InitializeComponent();
        BuildRows(items);
        UpdateCount();

        BtnSelectAll.Click += OnSelectAll;
        BtnSelectNone.Click += OnSelectNone;
    }

    /// <summary>用户确认后要更新的文件路径。</summary>
    public IReadOnlyList<string> SelectedPaths { get; private set; } = [];

    private void BuildRows(IReadOnlyList<ResourceUpdateReviewItem> items)
    {
        PanItems.ItemsSource = null;
        PanItems.Items.Clear();
        _rows.Clear();

        var panel = new StackPanel { Spacing = 8 };
        foreach (var item in items)
        {
            var box = new CheckBox
            {
                IsChecked = item.IsSelected,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = item.Name, FontSize = 13 },
                        new TextBlock { Text = item.Description, FontSize = 11, Opacity = 0.7 }
                    }
                }
            };
            box.IsCheckedChanged += (_, _) => UpdateCount();
            _rows.Add((item, box));
            panel.Children.Add(box);
        }

        if (items.Count == 0)
            panel.Children.Add(new TextBlock
            {
                Text = Lang.Text("Instance.Comp.Update.Review.Empty"),
                Opacity = 0.7
            });

        PanItems.Items.Add(panel);
    }

    private void UpdateCount()
    {
        var count = _rows.Count(row => row.Box.IsChecked == true);
        LabCount.Text = Lang.Text("Instance.Comp.Update.Review.Selected", count);
    }

    private void OnSelectAll(object? sender, PointerReleasedEventArgs? e)
    {
        foreach (var (_, box) in _rows) box.IsChecked = true;
        UpdateCount();
    }

    private void OnSelectNone(object? sender, PointerReleasedEventArgs? e)
    {
        foreach (var (_, box) in _rows) box.IsChecked = false;
        UpdateCount();
    }

    /// <summary>当前勾选的资源路径，弹窗确认后由调用方读取。</summary>
    public IReadOnlyList<string> GetSelectedPaths() => _rows
        .Where(row => row.Box.IsChecked == true)
        .Select(row => row.Item.FilePath)
        .ToList();
}
