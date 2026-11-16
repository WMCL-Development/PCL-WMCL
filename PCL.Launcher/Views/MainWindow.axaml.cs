using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using PCL.Core.App.IoC;
using PCL.Core.App.Localization;
using PCL.Core.App;
using PCL.Core.Logging;
using PCL.Core.UI;
using PCL.UI.SystemIntegration;
using PCL.Navigation;
using PCL.UI;
using PCL.Core.UI;
using PCL.Views.Pages;
using PCL.UI.SystemIntegration;
using PCL.Views.Shell;

namespace PCL.Views;

/// <summary>
///     主窗口：承载标题栏、左右页面宿主、背景层、提示与弹窗层，并接管窗口级交互与退出流程。
///     页面由 <see cref="PageRouter" /> 按 <see cref="IPageHost" /> 宿主挂载，窗口本身不感知具体页面。
/// </summary>
public partial class MainWindow : Window
{
    private const string LeftWidthAnimationKey = "MainWindow LeftChange";

    private readonly BackgroundLayer _background;
    private readonly ExtraButtonsPanel _extraButtons;
    private readonly TitleBar _titleBar;
    private bool _leftWidthUpdateQueued;

    public MainWindow()
    {
        InitializeComponent();
        Current = this;
        ApplyWindowIcon();
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Opacity = 0d;
        IsHitTestVisible = false;
        PanRoot.Opacity = 0d;

        _ = new WindowStateStore(this);
        ApplyDebugWindowSize();

        _titleBar = TitleBar;
        _extraButtons = PanExtraButtons;
        _background = new BackgroundLayer(PanForm, ImgBackImage);
        BackgroundHost.Attach(_background);
        _ = new FileDragHandler(this);
        _ = new KeyboardHandler(this, PageLeftHost);
        _ = new ExitFlow(this);

        PageRouter.Attach(PageLeftHost, PageRightHost);
        PreloadLaunchPage();
        PageRouter.Navigated += OnNavigated;
        PageRouter.PageHostSwapped += OnPageHostSwapped;
        PageRouter.PageChanged += _ => _extraButtons.RefreshAllStates();
        PageLeftHost.SizeChanged += (_, _) => ScheduleLeftWidthUpdate();
        LocalizationService.LanguageChanged += OnLanguageChangedForLeftWidth;

        ApplyRoundedWindowClip();
        AddHandler(
            PointerPressedEvent,
            OnResizePointerPressed,
            RoutingStrategies.Tunnel,
            true);
        PointerMoved += OnResizePointerMoved;

        Topmost = true;
        Opened += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                Topmost = false;
                Activate();
                Focus();
            },
            DispatcherPriority.Loaded);
        Opened += (_, _) => TaskbarProgress.SetThumbnailClip(new Thickness(10d));
        SizeChanged += (_, _) => TaskbarProgress.SetThumbnailClip(new Thickness(10d));

        _titleBar.TabSelected += PageRouter.Navigate;
        _titleBar.BackRequested += () => PageRouter.Back();
        _titleBar.HelpRequested += OpenHelp;
        _titleBar.MinimizeRequested += () => WindowState = WindowState.Minimized;
        _titleBar.CloseRequested += Close;
        Closing += OnWindowClosing;
        ApplyUiScale();
        SizeChanged += OnWindowSizeChangedForScale;
        GameLaunch.GameProcessStarted += OnGameProcessStarted;

        if (this.FindControl<Panel>("PanHint") is { } hintLayer)
            HintPresenter.Instance.Attach(this, hintLayer);
        if (this.FindControl<Panel>("PanDialogLayer") is { } dialogLayer)
            DialogPresenter.Instance.Attach(this, dialogLayer);

        Closed += (_, _) => OnClosed();

        Lifecycle.When(
            LifecycleState.Running,
            () => Dispatcher.UIThread.Post(() => PageRouter.Navigate(MainTab.Launch)));

        var effects = new StartupEffects(this, _titleBar, _background, _extraButtons);
        Lifecycle.When(LifecycleState.WindowCreated, effects.Run);

        try
        {
            GameFolders.McFolderListLoader.Start(0);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "UI", "启动文件夹列表加载失败");
        }

#if DEBUG
        DebugPageSmoke.TryStart();
#endif
    }

    public static MainWindow? Current { get; private set; }

    /// <summary>取标题栏，供外部在隐藏配置变化后刷新主标签。</summary>
    public PCL.Views.Shell.TitleBar? GetTitleBar() => _titleBar;
    /// <summary>
    ///     在窗口显示前预创建启动页，避免首屏导航时再同步实例化页面。
    /// </summary>
    private void PreloadLaunchPage()
    {
        try
        {
            if (PageRegistry.GetHost(PageType.Launch) is not { } host) return;
            var stack = new PageStackData(PageType.Launch);
            _ = host.GetLeft(stack);
            _ = host.GetRight(stack);
        }
        catch (Exception ex)
        {
            LogWrapper.Debug("UI", $"启动页预加载失败：{ex.Message}");
        }
    }

    private void OnNavigated(PageNavigationState state)
    {
        _titleBar.UpdateState(state);
        _extraButtons.RefreshAllStates();
    }

    private void OnPageHostSwapped()
    {
        ScheduleLeftWidthUpdate();
    }

    private void OnLanguageChangedForLeftWidth()
    {
        Dispatcher.UIThread.Post(ScheduleLeftWidthUpdate, DispatcherPriority.Loaded);
    }

    /// <summary>合并同一轮布局中的多次宽度变化，避免背景宽度来回抖动。</summary>
    private void ScheduleLeftWidthUpdate()
    {
        if (_leftWidthUpdateQueued)
            return;
        _leftWidthUpdateQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _leftWidthUpdateQueued = false;
                var width = PageLeftHost.IsVisible
                    ? PageLeftHost.Bounds.Width > 0d
                        ? PageLeftHost.Bounds.Width
                        : PageLeftHost.DesiredSize.Width
                    : 0d;
                AnimateLeftWidth(width);
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    ///     仅用于截图/布局证据：PCL_DEBUG_WINDOW_SIZE=810x470 或 workarea。
    ///     不实现真正的最大化；UI 设计中没有最大化场景。
    /// </summary>
    private void ApplyDebugWindowSize()
    {
        var value = Environment.GetEnvironmentVariable("PCL_DEBUG_WINDOW_SIZE");
        if (string.IsNullOrWhiteSpace(value)) return;

        if (value.Equals("workarea", StringComparison.OrdinalIgnoreCase))
        {
            var screen = Screens.Primary;
            if (screen is null) return;
            var scaling = screen.Scaling <= 0d ? 1d : screen.Scaling;
            Width = Math.Max(MinWidth, screen.WorkingArea.Width / scaling);
            Height = Math.Max(MinHeight, screen.WorkingArea.Height / scaling);
            return;
        }

        var parts = value.Split('x', 'X');
        if (parts.Length != 2
            || !double.TryParse(parts[0], out var width)
            || !double.TryParse(parts[1], out var height))
            return;

        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);
    }

    private void ApplyRoundedWindowClip()
    {
        PanBack.SizeChanged += (_, e) =>
        {
            PanBack.Clip = new RectangleGeometry
            {
                Rect = new Rect(e.NewSize),
                RadiusX = 6d,
                RadiusY = 6d
            };
        };
    }

    private void OnResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!CanResize
            || WindowState == WindowState.Maximized
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (GetResizeEdge(e.GetPosition(this)) is not { } edge) return;
        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void OnResizePointerMoved(object? sender, PointerEventArgs e)
    {
        Cursor = !CanResize || WindowState == WindowState.Maximized
            ? null
            : GetResizeEdge(e.GetPosition(this)) switch
            {
                WindowEdge.NorthWest => new Cursor(StandardCursorType.TopLeftCorner),
                WindowEdge.North => new Cursor(StandardCursorType.TopSide),
                WindowEdge.NorthEast => new Cursor(StandardCursorType.TopRightCorner),
                WindowEdge.West => new Cursor(StandardCursorType.LeftSide),
                WindowEdge.East => new Cursor(StandardCursorType.RightSide),
                WindowEdge.SouthWest => new Cursor(StandardCursorType.BottomLeftCorner),
                WindowEdge.South => new Cursor(StandardCursorType.BottomSide),
                WindowEdge.SouthEast => new Cursor(StandardCursorType.BottomRightCorner),
                _ => null
            };
    }

    private WindowEdge? GetResizeEdge(Point point)
    {
        const double band = 8d;
        var width = Bounds.Width;
        var height = Bounds.Height;
        var left = point.X <= band;
        var right = point.X >= width - band;
        var top = point.Y <= band;
        var bottom = point.Y >= height - band;

        if (top && left) return WindowEdge.NorthWest;
        if (top && right) return WindowEdge.NorthEast;
        if (bottom && left) return WindowEdge.SouthWest;
        if (bottom && right) return WindowEdge.SouthEast;
        if (top) return WindowEdge.North;
        if (bottom) return WindowEdge.South;
        if (left) return WindowEdge.West;
        if (right) return WindowEdge.East;
        return null;
    }

    private void AnimateLeftWidth(double target)
    {
        var current = double.IsNaN(RectLeftBackground.Width)
            ? RectLeftBackground.Bounds.Width
            : RectLeftBackground.Width;

        if (Math.Abs(target - current) < 0.1d
            || !IsVisible
            || !AnimationService.IsEnabled
            || !RectLeftBackground.IsLoaded)
        {
            AnimationService.CancelAnimationByName(LeftWidthAnimationKey);
            RectLeftBackground.Width = target;
            return;
        }

        AnimationService.Run(
            new List<IAnimation>
            {
                new WidthAnimation(
                    RectLeftBackground,
                    target - current,
                    180,
                    ease: new FluentEaseOut(EasePower.ExtraStrong))
            },
            LeftWidthAnimationKey,
            true);
    }

    private void OnClosed()
    {
        PageRouter.Navigated -= OnNavigated;
        PageRouter.PageHostSwapped -= OnPageHostSwapped;
        LocalizationService.LanguageChanged -= OnLanguageChangedForLeftWidth;
        BackgroundHost.Detach(_background);
        _background.Remove();
        if (ReferenceEquals(Current, this)) Current = null;
    }

    private TrayService? _tray;
    private bool _exitingFromTray;

    /// <summary>关闭窗口：开了「关闭到托盘」就藏起来，否则真正退出。</summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exitingFromTray) return;
        if (!Config.Preference.CloseToTray) return;

        _tray ??= TrayService.Attach(this);
        if (!_tray.IsAvailable) return;

        e.Cancel = true;
        Hide();
    }

    /// <summary>游戏启动后按设置处理启动器窗口：保持 / 最小化 / 隐藏。</summary>
    private void OnGameProcessStarted(int processId)
    {
        switch (Config.Preference.LauncherAfterLaunch)
        {
            case "minimize":
                WindowState = WindowState.Minimized;
                break;
            case "hide":
                Hide();
                break;
        }
    }

    /// <summary>
    ///     按配置应用界面缩放（80%–150%）。只缩放内容，不改窗口尺寸；
    ///     窗口装不下放大后的内容时按可用空间收敛，避免被裁掉。
    /// </summary>
    public void ApplyUiScale()
    {
        var requested = Math.Clamp(Config.Preference.UiScalePercent, 80, 150) / 100d;
        var availableWidth = Math.Max(Bounds.Width, 1d) / BaseWidth;
        var availableHeight = Math.Max(Bounds.Height, 1d) / BaseHeight;
        var fit = Math.Min(availableWidth, availableHeight);
        var scale = Math.Clamp(Math.Min(requested, Math.Max(fit, 0.8d)), 0.8d, 1.5d);

        PanScale.LayoutTransform = Math.Abs(scale - 1d) < 0.001
            ? null
            : new ScaleTransform(scale, scale);
    }

    private void OnWindowSizeChangedForScale(object? sender, SizeChangedEventArgs e) => ApplyUiScale();

    /// <summary>未缩放时的基准内容尺寸，用于判断窗口能否装下放大后的内容。</summary>
    private const double BaseWidth = 850d;

    private const double BaseHeight = 500d;

    private void ApplyWindowIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(
                new Uri("avares://PCL.Launcher/Assets/Images/icon.ico"));
            Icon = new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            LogWrapper.Warn("UI", $"加载窗口图标失败：{ex.Message}");
        }
    }

    private static void OpenHelp()
    {
        try
        {
            Basics.OpenUrl(Basics.HelpUrl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UI] 打开帮助失败：{ex.Message}");
        }
    }
}
