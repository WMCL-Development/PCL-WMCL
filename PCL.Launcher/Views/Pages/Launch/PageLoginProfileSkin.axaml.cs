using System.ComponentModel;
using Avalonia.Controls;
using PCL.UI;
using PCL.UI.Controls;

namespace PCL.Views.Pages.Launch;

/// <summary>
/// 当前账号视图：皮肤头像、玩家名与验证方式，并提供皮肤与披风、修改信息、选择档案三个入口。 档案信息与菜单命令由
/// <see cref="PageLoginProfileSkinViewModel" /> 负责，页面保留菜单、 悬停动画与皮肤控件交互。
/// </summary>
public partial class PageLoginProfileSkin : LoginPageBase
{
    private readonly MyIconButton _btnEdit;
    private readonly MyIconButton _btnSelect;
    private readonly MyIconButton _btnSkin;
    private readonly MyCard _panButtons;
    private readonly MySkin _skin;

    public PageLoginProfileSkin()
    {
        InitializeComponent();
        DataContext = ViewModel;
        _skin = this.FindControl<MySkin>("Skin")!;
        _panButtons = this.FindControl<MyCard>("PanButtons")!;
        _btnSkin = this.FindControl<MyIconButton>("BtnSkin")!;
        _btnEdit = this.FindControl<MyIconButton>("BtnEdit")!;
        _btnSelect = this.FindControl<MyIconButton>("BtnSelect")!;

        BuildSkinMenu();
        BuildEditMenu();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.SaveSkinRequested += () => _ = _skin.SaveSkinAsync();
        ViewModel.RefreshSkinRequested += () => _skin.RefreshSource();
        ViewModel.RefreshRequested += RequestRefresh;

        _btnSkin.Click += (_, _) => _btnSkin.ContextMenu?.Open(_btnSkin);
        _btnEdit.Click += (_, _) => _btnEdit.ContextMenu?.Open(_btnEdit);
        ContextMenuHelper.Attach(_btnSkin, () => _btnSkin.ContextMenu);
        ContextMenuHelper.Attach(_btnEdit, () => _btnEdit.ContextMenu);
        _btnSelect.Click += (_, _) => ViewModel.SelectProfileCommand.Execute(null);

        PointerEntered += (_, _) => ShowButtons();
        PointerExited += (_, e) =>
        {
            // 触摸屏没有悬停，移出事件不代表用户离开，交给点击切换。
            if (e.Pointer.Type != Avalonia.Input.PointerType.Mouse)
                return;
            if (this.IsHoveredState() || IsMenuOpen())
                return;
            HideButtons();
        };
        // 触摸屏上点一下头像区域切换按钮面板（桌面鼠标行为不变）。
        PointerReleased += (_, e) =>
        {
            if (e.Pointer.Type == Avalonia.Input.PointerType.Mouse)
                return;
            if (IsMenuOpen())
                return;

            if (_panButtons.Opacity < 0.5d)
                ShowButtons();
            else
                HideButtons();
        };
    }

    public PageLoginProfileSkinViewModel ViewModel { get; } = new();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PageLoginProfileSkinViewModel.SkinAddress):
                if (string.IsNullOrEmpty(ViewModel.SkinAddress))
                    _skin.Clear();
                else
                    _skin.Address = ViewModel.SkinAddress;
                break;
        }
    }

    private void ShowButtons()
    {
        AnimationService.Run(
            new OpacityAnimation(_panButtons, 1d - _panButtons.Opacity, 120),
            "PageLoginProfileSkin Button");
    }

    private void HideButtons()
    {
        AnimationService.Run(
            new OpacityAnimation(_panButtons, -_panButtons.Opacity, 120),
            "PageLoginProfileSkin Button");
    }

    private bool IsMenuOpen()
    {
        return _btnSkin.ContextMenu?.IsOpen == true || _btnEdit.ContextMenu?.IsOpen == true;
    }

    public override void Reload()
    {
        _ = ViewModel.ReloadAsync();
    }

    #region 菜单

    private void BuildSkinMenu()
    {
        var items = new List<MyMenuItem>();

        // 菜单只保留两项：进入皮肤子页、刷新头像；其余操作都收进皮肤子页。
        AddItem(
            "Launch.Skin.Title",
            "lucide/shirt",
            OpenSkinPage);
        AddItem(
            "Launch.Account.ProfileSkin.RefreshSkin",
            "lucide/refresh-cw",
            () => ViewModel.RefreshSkinCommand.Execute(null));

        _btnSkin.ContextMenu = new ContextMenu
        {
            ItemsSource = items,
            HorizontalOffset = 10d,
            VerticalOffset = 18d
        };
        _btnSkin.ContextMenu.Closed += (_, _) =>
        {
            if (!this.IsHoveredState()) HideButtons();
        };
        return;

        MyMenuItem AddItem(string headerKey, string svgIcon, Action action)
        {
            var item = new MyMenuItem
            {
                SvgIcon = svgIcon
            };
            item.SetLocalizedValue(MenuItem.HeaderProperty, headerKey);
            item.Click += (_, _) =>
            {
                action();
                HideButtons();
            };
            items.Add(item);
            return item;
        }
    }

    /// <summary>打开皮肤管理：Android 用独立子页，桌面保持原有的更换皮肤弹窗。</summary>
    private void OpenSkinPage()
    {
        if (OperatingSystem.IsAndroid())
        {
            PCL.Navigation.PageRouter.Navigate(PCL.Navigation.PageIds.LaunchSkin);
            return;
        }

        ViewModel.ChangeSkinCommand.Execute(null);
    }

    private void BuildEditMenu()
    {
        var items = new List<MyMenuItem>();

        AddItem(
            "Launch.Account.ProfileSkin.EditPassword",
            "lucide/key-round",
            () => ViewModel.EditPasswordCommand.Execute(null));
        AddItem(
            "Launch.Account.ProfileSkin.EditUsername",
            "lucide/user-pen",
            () => ViewModel.EditUserNameCommand.Execute(null));

        _btnEdit.ContextMenu = new ContextMenu
        {
            ItemsSource = items,
            HorizontalOffset = 10d,
            VerticalOffset = 18d
        };
        _btnEdit.ContextMenu.Closed += (_, _) =>
        {
            if (!this.IsHoveredState()) HideButtons();
        };
        return;

        void AddItem(string headerKey, string svgIcon, Action action)
        {
            var item = new MyMenuItem
            {
                SvgIcon = svgIcon
            };
            item.SetLocalizedValue(MenuItem.HeaderProperty, headerKey);
            item.Click += (_, _) =>
            {
                action();
                HideButtons();
            };
            items.Add(item);
        }
    }

    #endregion
}