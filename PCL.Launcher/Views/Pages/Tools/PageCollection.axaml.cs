using Avalonia.Controls;
using Avalonia.Threading;
using PCL.Core.App.Localization;
using PCL.UI.Controls;
using PCL.UI.Controls.Mc;

namespace PCL.Views.Pages.Tools;

/// <summary>百宝箱 › 收藏库：原弹窗改为独立二级页，加载时显示居中遮罩。</summary>
public partial class PageCollection : MyPageRight
{
    public PageCollection()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        PanMask.IsVisible = true;
        LabLoading.Text = Lang.Text("Common.State.Loading");
        try
        {
            if (PCL.Views.Pages.Tools.PageToolsTest.Current?.ViewModel is not { } viewModel)
                return;

            await viewModel.EnsureInventoryAssetsAsync();
            var (items, counts) = viewModel.BuildCollectionInventory();

            var inventory = new McCreativeInventory
            {
                Scale = 2,
                Width = 390,
                Height = 384,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            inventory.Items = items;
            inventory.SetLayout(counts, viewModel.CollectionTabIcons);

            PanContent.Child = inventory;
        }
        catch (Exception exception)
        {
            AndroidLog(exception);
            LabLoading.Text = Lang.Text("Common.State.Failed");
            return;
        }
        finally
        {
            PanMask.IsVisible = false;
        }
    }

    private static void AndroidLog(Exception exception) =>
        PCL.Core.Logging.LogWrapper.Warn("Tools", "收藏库加载失败：" + exception.Message);
}
