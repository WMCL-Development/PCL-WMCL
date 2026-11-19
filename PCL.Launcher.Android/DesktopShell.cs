using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PCL.Core.UI.Animation;
using PCL.Core.UI.Animation.Core;
using PCL.Core.UI.Animation.Easings;
using PCL.Navigation;
using PCL.UI;
using PCL.Views.Shell;

namespace PCL.Android;

/// <summary>
///     Android 外壳：按桌面端 <c>MainWindow</c> 的视觉结构重建
///     （标题栏 / 左栏背景 / 左右栏宿主 / 浮动按钮 / 提示层 / 弹窗层），
///     窗口专属能力（最小化、关闭、拖动、背景层、缩放）在 Android 上不适用。
/// </summary>
public sealed class DesktopShell : UserControl
{
    private readonly ContentControl _leftHost = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        IsVisible = false
    };

    private readonly ContentControl _rightHost = new();

    private readonly Border _leftBackground = new()
    {
        Width = 0,
        HorizontalAlignment = HorizontalAlignment.Left,
        IsHitTestVisible = false,
        BoxShadow = BoxShadows.Parse("4 0 6 0 #0A000000")
    };

    public DesktopShell()
    {
        var panMain = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            // 页面内容比可用区域高时不要画到标题栏上（小屏 + 界面缩放时容易出现）。
            ClipToBounds = true
        };
        Grid.SetColumn(_leftHost, 0);
        panMain.Children.Add(_leftHost);
        Grid.SetColumn(_rightHost, 1);
        panMain.Children.Add(_rightHost);

        // 与桌面 PanForm 一致：Row0 标题栏，Row1 左栏背景 + 页面区 + 浮动按钮 + 提示层 + 弹窗层。
        var form = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        // 背景层：桌面用 ImgBack + ImgBackImage 承载渐变/背景图，这里保持同样的结构。
        var imgBack = new Grid
        {
            [Grid.RowProperty] = 1,
            ClipToBounds = true,
            IsHitTestVisible = false
        };
        var imgBackImage = new Image
        {
            Name = "ImgBackImage",
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        imgBack.Children.Add(imgBackImage);
        form.Children.Add(imgBack);

        try
        {
            var titleBar = new TitleBar();
            titleBar.FindControl<Control>("PanCaptionButtons")?.SetValue(IsVisibleProperty, false);

            // 与桌面 MainWindow 一致：标签切换、返回、状态刷新都走路由。
            titleBar.TabSelected += PageRouter.Navigate;
            titleBar.BackRequested += () => PageRouter.Back();
            PageRouter.Navigated += titleBar.UpdateState;

            Grid.SetRow(titleBar, 0);
            form.Children.Add(titleBar);
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("shell:TitleBar", exception);
        }

        _leftBackground.Bind(BackgroundProperty,
            new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
                "ColorBrushTransparentBackground"));
        Grid.SetRow(_leftBackground, 1);
        form.Children.Add(_leftBackground);

        Grid.SetRow(panMain, 1);
        form.Children.Add(panMain);

        // 提示层：右下角堆叠提示气泡。
        var hintLayer = new StackPanel
        {
            Name = "PanHint",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var hintAnchor = new Grid
        {
            [Grid.RowSpanProperty] = 2,
            ZIndex = 1,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        hintAnchor.Children.Add(hintLayer);
        form.Children.Add(hintAnchor);

        // 浮动按钮列（下载球等），与桌面一样位于页面区。
        try
        {
            var extra = new ExtraButtonsPanel { Name = "PanExtraButtons" };
            Grid.SetRow(extra, 1);
            form.Children.Add(extra);
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("shell:ExtraButtons", exception);
        }

        // 弹窗层：遮罩 + 居中容器，控件名称与桌面保持一致，供 DialogPresenter 按名查找。
        var dialogHost = new Panel
        {
            Name = "PanDialogHost",
            MinWidth = 400,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var dialogMask = new Border
        {
            Name = "BorDialogMask",
            Background = new SolidColorBrush(Color.Parse("#00000000"))
        };
        var dialogLayer = new Panel
        {
            Name = "PanDialogLayer",
            [Grid.RowSpanProperty] = 2,
            IsVisible = false,
            Opacity = 0
        };
        dialogLayer.Children.Add(dialogMask);
        dialogLayer.Children.Add(dialogHost);
        form.Children.Add(dialogLayer);

        // Android 没有窗口外框，内容直接铺满，圆角与边距只在桌面窗口上才有意义。
        var back = new Border
        {
            Margin = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            ClipToBounds = true,
            Child = form
        };

        // 界面缩放：手机逻辑尺寸（如 914×411）比桌面基准（850×500）矮，
        // 按可用空间等比缩小内容，避免弹窗/页面被裁掉。
        var scaleHost = new LayoutTransformControl { Child = back };
        Content = new Panel { Children = { scaleHost } };

        ApplyUiScale(scaleHost);

        // 设置页修改「界面缩放」后重新应用。
        PCL.UI.UiScaleNotifier.Changed += () => Dispatcher.UIThread.Post(() => ApplyUiScale(scaleHost));

        // Avalonia 默认把安全区（含摄像头挖孔）作为 Padding 加到 TopLevel 子控件上，
        // 这里关掉，让内容真正铺满整屏。
        TopLevel.SetAutoSafeAreaPadding(this, false);

        // 让窗口本身也进入 edge-to-edge（含挖孔区），Avalonia 需要 TopLevel 就绪后才能拿到 InsetsManager。
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.InsetsManager is { } insets)
                {
                    insets.DisplayEdgeToEdgePreference = true;
                    // 隐藏状态栏与导航栏，向下滑动可临时唤出。
                    insets.IsSystemBarVisible = false;
                }
            }
            catch (Exception exception)
            {
                AndroidBootstrap.RecordFatal("shell:EdgeToEdge", exception);
            }
        });

        // 关键：把左右栏宿主交给路由，否则 NavigateCore 会因 _rightHost 为空直接返回。
        PageRouter.Attach(_leftHost, _rightHost);

        // 无窗口平台：弹窗与提示直接渲染到层内。
        DialogPresenter.Instance.Attach(dialogLayer);
        HintPresenter.Instance.Attach(hintLayer);

        // 背景层：按配置绘制颜色主题渐变或背景图片。
        try
        {
            var background = new BackgroundLayer(form, imgBackImage);
            BackgroundHost.Attach(background);
            background.Refresh();
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("shell:Background", exception);
        }

        // 触摸屏没有右键：长按映射为右键菜单。
        LongPressContextMenu.Install(this);
        TouchHover.Install(this);

        // 左栏背景宽度跟随左栏实际宽度；左栏隐藏时归零，避免残留一条白色列。
        // 切换页面时用宽度动画过渡，避免"闪一下"。
        void SyncLeftBackground()
        {
            var target = _leftHost.IsVisible ? _leftHost.Bounds.Width : 0d;
            var current = double.IsNaN(_leftBackground.Width)
                ? _leftBackground.Bounds.Width
                : _leftBackground.Width;

            if (Math.Abs(target - current) < 0.1d || !_leftBackground.IsLoaded)
            {
                AnimationService.CancelAnimationByName("LeftWidth");
                _leftBackground.Width = target;
                return;
            }

            AnimationService.Run(
                new List<IAnimation>
                {
                    new WidthAnimation(
                        _leftBackground,
                        target - current,
                        180,
                        ease: new FluentEaseOut(EasePower.ExtraStrong))
                },
                "LeftWidth",
                true);
        }

        _leftHost.SizeChanged += (_, _) => SyncLeftBackground();
        _leftHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty) SyncLeftBackground();
        };
        PageRouter.Navigated += _ => Dispatcher.UIThread.Post(SyncLeftBackground);

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                PageRouter.Navigate(MainTab.Launch);
            }
            catch (Exception exception)
            {
                _rightHost.Content = new DiagnosticView(exception.ToString());
            }
        });
    }

    /// <summary>按设置里的「界面缩放」百分比缩放内容，默认 100%（不缩放）。</summary>
    private static void ApplyUiScale(LayoutTransformControl host)
    {
        var scale = Math.Clamp(PCL.Core.App.Config.Preference.UiScalePercent, 80, 150) / 100d;
        host.LayoutTransform = Math.Abs(scale - 1d) < 0.01d
            ? null
            : new ScaleTransform(scale, scale);
    }
}
