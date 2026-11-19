using Avalonia.Controls;
using Avalonia.Threading;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.UI.Controls;
using PCL.UI.Controls.Mc;

namespace PCL.Views.Pages.Tools;

/// <summary>百宝箱 › 选择物品：原弹窗改为独立二级页，加载时显示居中遮罩。</summary>
public partial class PageItemPicker : MyPageRight
{
    public PageItemPicker()
    {
        InitializeComponent();
        BtnConfirm.Click += (_, _) => PCL.Navigation.PageRouter.Back();
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

            await viewModel.PrepareLotteryAsync();
            await viewModel.EnsureInventoryAssetsAsync();
            var (items, counts, icons) = viewModel.BuildItemPicker();

            var inventory = new McCreativeInventory
            {
                Scale = 2,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            inventory.Items = items;
            inventory.SetLayout(counts, icons);
            inventory.SelectedId = viewModel.AchievementItem;
            inventory.ItemActivated += (_, slot) =>
            {
                viewModel.AchievementItem = slot.Id;
                inventory.SelectedId = slot.Id;
                LabSelected.Text = slot.Id;
            };

            LabSelected.Text = viewModel.AchievementItem;
            PanContent.Child = inventory;
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Tools", "选择物品加载失败：" + exception.Message);
            LabLoading.Text = Lang.Text("Common.State.Failed");
            return;
        }
        finally
        {
            PanMask.IsVisible = false;
        }
    }
}
