using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using PCL.UI;
using PCL.UI.Controls;

namespace PCL.Views.Pages.Download;

public sealed partial class MyCompItem : Grid
{
    private readonly int _uuid = LauncherCommon.GetUuid();
    private bool _isMouseDown;
    private bool _touchButtonsVisible;
    private string? _stateLast;

    public MyCompItem()
    {
        ViewModel = null!;
        Project = null!;
        InitializeComponent();
    }

    public MyCompItem(
        Comp.CompProject project,
        bool showMcVersionDesc = true,
        bool showLoaderDesc = true,
        bool showQuickDownload = false)
        : this(
            MyCompItemViewModel.Create(
                project,
                showMcVersionDesc,
                showLoaderDesc,
                showQuickDownload),
            project)
    {
    }

    public MyCompItem(MyCompItemViewModel viewModel) : this(
        viewModel,
        viewModel.Project ?? throw new ArgumentNullException(nameof(viewModel)))
    {
    }

    private MyCompItem(MyCompItemViewModel viewModel, Comp.CompProject project)
    {
        ViewModel = viewModel;
        Project = project;
        InitializeComponent();

        Cursor = new Cursor(StandardCursorType.Hand);
        ApplyLogo(project);
        Tags = viewModel.Tags;
        ApplyStatsVisibility(viewModel);
        BtnDownload.IsVisible = viewModel.ShowDownloadButton;
        BtnFavorite.SvgIcon = viewModel.FavoriteIcon;
        BtnFavorite.IsVisible = viewModel.ShowFavoriteButton;
        UpdatePanButtons();

        LabInfo.PointerEntered += (_, _) => UpdateDescriptionToolTip();
        // 触摸屏没有悬停，按钮透明度一直是 0，不能拿它当可点击门槛。
        BtnDownload.Click += (_, _) =>
        {
            if (CanInteraction)
                Comp.QuickDownload(Project);
        };
        BtnFavorite.Click += (_, _) =>
        {
            if (CanInteraction)
                CompFavoritesMenu.Show(Project, BtnFavorite, RefreshFavoriteStatus);
        };

        PointerEntered += (_, _) => RefreshColor();
        PointerExited += (_, _) =>
        {
            _isMouseDown = false;
            RefreshColor();
        };
        PointerPressed += Button_MouseDown;
        PointerReleased += Button_MouseUp;
        PointerCaptureLost += (_, _) =>
        {
            _isMouseDown = false;
            RefreshColor();
        };
        Loaded += (_, _) =>
        {
            _stateLast = null;
            RefreshColor();
        };
        ViewModel.ClickRequested += _ => Click?.Invoke(this);
    }

    public Comp.CompProject Project { get; }

    public MyCompItemViewModel ViewModel { get; }

    public bool CanInteraction { get; set; } = true;

    /// <summary>标签列表，内容为本地化资源键。</summary>
    public IEnumerable<string>? Tags
    {
        set =>
            TagPanelHelper.FillLocalized(
                PanTags,
                value,
                new SolidColorBrush(Color.FromRgb(134, 134, 134)));
    }

    public bool ShowDownloadBtn
    {
        get => BtnDownload.IsVisible;
        set
        {
            BtnDownload.IsVisible = value;
            UpdatePanButtons();
        }
    }

    public bool ShowFavoriteBtn
    {
        get => BtnFavorite.IsVisible;
        set
        {
            BtnFavorite.IsVisible = value;
            UpdatePanButtons();
        }
    }

    private void UpdatePanButtons()
    {
        var show = ShowFavoriteBtn || ShowDownloadBtn;
        PanButtons.IsVisible = show;
        if (!show)
            PanButtons.Opacity = 0d;
    }

    private void ApplyStatsVisibility(MyCompItemViewModel viewModel)
    {
        var showVersion = viewModel.ShowVersion && !string.IsNullOrEmpty(viewModel.VersionText);
        var showDownload = viewModel.ShowDownload && !string.IsNullOrEmpty(viewModel.DownloadText);
        var showTime = viewModel.ShowTime && !string.IsNullOrEmpty(viewModel.TimeText);
        var showSource = viewModel.ShowSource && !string.IsNullOrEmpty(viewModel.SourceText);

        SvgIconVersion.IsVisible = showVersion;
        LabVersion.IsVisible = showVersion;
        SvgIconDownload.IsVisible = showDownload;
        LabDownload.IsVisible = showDownload;
        SvgIconTime.IsVisible = showTime;
        LabTime.IsVisible = showTime;
        SvgIconSource.IsVisible = showSource;
        LabSource.IsVisible = showSource;

        var columns = PanStats.ColumnDefinitions;
        SetStatColumns(columns, 0, showVersion);
        SetStatColumns(columns, 3, showDownload);
        SetStatColumns(columns, 6, showTime);
        SetStatColumns(columns, 9, showSource);
    }

    private static void SetStatColumns(ColumnDefinitions columns, int start, bool show)
    {
        columns[start].Width = show ? GridLength.Auto : new GridLength(0d);
        columns[start + 1].Width = show ? GridLength.Auto : new GridLength(0d);
        columns[start + 2].Width = show
            ? new GridLength(0.25d, GridUnitType.Star)
            : new GridLength(0d);
    }

    private void UpdateDescriptionToolTip()
    {
        var text = LabInfo.Text;
        if (string.IsNullOrEmpty(text) || LabInfo.Bounds.Width <= 0d)
        {
            ViewModel.SetDescriptionToolTip(null);
            ToolTip.SetTip(LabInfo, null);
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                LabInfo.FontFamily,
                LabInfo.FontStyle,
                LabInfo.FontWeight,
                LabInfo.FontStretch),
            LabInfo.FontSize,
            LabInfo.Foreground);
        var tooltip = formatted.WidthIncludingTrailingWhitespace
            > LabInfo.Bounds.Width + 0.5d
                ? text
                : null;
        ViewModel.SetDescriptionToolTip(tooltip);
        ToolTip.SetTip(LabInfo, tooltip);
    }

    public event Action<MyCompItem>? Click;

    public void RefreshFavoriteStatus()
    {
        ViewModel.RefreshFavoriteStatus();
        BtnFavorite.SvgIcon = ViewModel.FavoriteIcon;
    }

    private void Button_MouseDown(object? sender, PointerPressedEventArgs e)
    {
        if (!CanInteraction
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || IsPointerOnActionButton(e.Source as Visual))
            return;

        _isMouseDown = true;
        RefreshColor();
    }

    /// <summary>触摸屏：切换右侧按钮组的显示状态。</summary>
    private void SetTouchButtonsVisible(bool show)
    {
        _touchButtonsVisible = show;
        PanButtons.IsHitTestVisible = show;
        PanButtons.Opacity = show ? 1d : 0d;
        RefreshColor();
    }

    private void Button_MouseUp(object? sender, PointerReleasedEventArgs e)
    {
        var wasMouseDown = _isMouseDown;
        _isMouseDown = false;

        // 触摸屏右侧 1/3：展开/收起按钮组，不触发行点击（与实例列表一致）。
        if (e.Pointer.Type != PointerType.Mouse
            && Bounds.Width > 0
            && e.GetPosition(this).X > Bounds.Width * 2d / 3d)
        {
            if (CanInteraction)
                SetTouchButtonsVisible(!_touchButtonsVisible);
            e.Handled = true;
            return;
        }

        if (wasMouseDown
            && CanInteraction
            && e.InitialPressMouseButton == MouseButton.Left
            && !IsPointerOnActionButton(e.Source as Visual))
        {
            if (_touchButtonsVisible)
                SetTouchButtonsVisible(false);
            ViewModel.ClickCommand.Execute(null);
        }

        RefreshColor();
    }

    private void RefreshColor()
    {
        if (!CanInteraction)
        {
            AnimationService.CancelAnimationByName("CompItem Color " + _uuid);
            _stateLast = null;
            RectBack.Opacity = 0d;
            PanButtons.Opacity = 0d;
            return;
        }

        var stateNew = this.IsHoveredState()
            ? _isMouseDown ? "MouseDown" : "MouseOver"
            : "Idle";
        if ((_stateLast ?? string.Empty) == stateNew)
            return;
        _stateLast = stateNew;

        var time = stateNew == "Idle" ? 180 : 120;
        if (!ContainerAnimation.CanAnimate(this))
        {
            ApplyColorStateInstant(stateNew);
            return;
        }

        var animations = new List<IAnimation>();
        var hasButtons = ShowFavoriteBtn || ShowDownloadBtn;
        var showButtons = hasButtons && (_touchButtonsVisible || stateNew is "MouseOver" or "MouseDown");
        PanButtons.IsHitTestVisible = hasButtons && (showButtons || IsPointerOver);
        var scale = EnsureBackScale();

        if (stateNew is "MouseOver" or "MouseDown")
        {
            if (showButtons)
                animations.Add(
                    new OpacityAnimation(
                        PanButtons,
                        1d - PanButtons.Opacity,
                        (int)Math.Round(time * 0.35d),
                        (int)Math.Round(time * 0.15d)));

            animations.Add(
                new ColorAnimation(
                    RectBack,
                    Border.BackgroundProperty,
                    stateNew == "MouseDown" ? "ColorBrush6" : "ColorBrushBg1",
                    time));
            animations.Add(
                new OpacityAnimation(
                    RectBack,
                    1d - RectBack.Opacity,
                    time,
                    ease: new FluentEaseOut()));
            animations.Add(
                new ScaleTransformAnimation(
                    RectBack,
                    (stateNew == "MouseDown" ? 0.996d : 1d) - scale.ScaleX,
                    (int)Math.Round(time * 1.2d),
                    ease: new FluentEaseOut()));
        }
        else
        {
            if (showButtons)
                animations.Add(
                    new OpacityAnimation(
                        PanButtons,
                        1d - PanButtons.Opacity,
                        (int)Math.Round(time * 0.3d)));
            else if (hasButtons)
                animations.Add(
                    new OpacityAnimation(
                        PanButtons,
                        -PanButtons.Opacity,
                        (int)Math.Round(time * 0.4d)));

            animations.Add(new OpacityAnimation(RectBack, -RectBack.Opacity, time));
            animations.Add(
                new ColorAnimation(
                    RectBack,
                    Border.BackgroundProperty,
                    "ColorBrush7",
                    time));
            animations.Add(
                new ScaleTransformAnimation(
                    RectBack,
                    0.996d - scale.ScaleX,
                    time,
                    ease: new FluentEaseOut()));
            animations.Add(new ScaleTransformAnimation(RectBack, -0.196d, 1, after: true));
        }

        AnimationService.Run(animations, "CompItem Color " + _uuid);
    }

    private void ApplyColorStateInstant(string stateNew)
    {
        AnimationService.CancelAnimationByName("CompItem Color " + _uuid);

        var hasButtons = ShowFavoriteBtn || ShowDownloadBtn;
        var showButtons = hasButtons && (_touchButtonsVisible || stateNew is "MouseOver" or "MouseDown");
        PanButtons.Opacity = showButtons ? 1d : 0d;
        PanButtons.IsHitTestVisible = hasButtons && (showButtons || IsPointerOver);
        RectBack.Opacity = stateNew == "Idle" ? 0d : 1d;
        ContainerTheme.BindDynamic(
            RectBack,
            Border.BackgroundProperty,
            stateNew == "MouseDown"
                ? "ColorBrush6"
                : stateNew == "MouseOver"
                    ? "ColorBrushBg1"
                    : "ColorBrush7");

        var scale = EnsureBackScale();
        var scaleValue = stateNew == "Idle" ? 0.8d : stateNew == "MouseDown" ? 0.996d : 1d;
        scale.ScaleX = scaleValue;
        scale.ScaleY = scaleValue;
    }

    private ScaleTransform EnsureBackScale()
    {
        if (RectBack.RenderTransform is ScaleTransform scale)
            return scale;

        scale = new ScaleTransform(1d, 1d);
        RectBack.RenderTransform = scale;
        RectBack.RenderTransformOrigin = RelativePoint.Center;
        return scale;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AnimationService.CancelAnimationByName("CompItem Color " + _uuid);
        _isMouseDown = false;
        _stateLast = null;
        base.OnDetachedFromVisualTree(e);
    }

    private bool IsPointerOnActionButton(Visual? source)
    {
        for (var current = source;
             current is not null && current != this;
             current = current.GetVisualParent())
            if (current is MyIconButton)
                return true;
        return false;
    }

    private void ApplyLogo(Comp.CompProject project)
    {
        if (string.IsNullOrEmpty(project.LogoUrl))
        {
            PathLogo.Source = "avares://PCL.Launcher/Assets/Images/Icons/NoIcon.png";
            return;
        }

        PathLogo.Source = project.LogoUrl;
        try
        {
            var fallback = GameDownload.DlSourceModGet(project.LogoUrl);
            if (!string.IsNullOrEmpty(fallback))
                PathLogo.FallbackSource = fallback;
        }
        catch (Exception)
        {
            // ignored
        }
    }
}