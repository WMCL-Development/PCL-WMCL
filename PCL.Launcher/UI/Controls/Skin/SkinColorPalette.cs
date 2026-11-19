using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PCL.Core.App.Localization;
using PCL.UI.Controls;

namespace PCL.UI.Controls.Skin;

/// <summary>HSV 明度/饱和度取色区域：触摸与鼠标拖拽都可用。</summary>
public sealed class SkinSaturationField : Control
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<SkinSaturationField, double>(nameof(Hue));

    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<SkinSaturationField, double>(nameof(Saturation));

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SkinSaturationField, double>(nameof(Value), 1);

    public double Hue { get => GetValue(HueProperty); set => SetValue(HueProperty, value); }

    public double Saturation { get => GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public event EventHandler? ColorPositionChanged;

    private bool _dragging;

    static SkinSaturationField()
    {
        AffectsRender<SkinSaturationField>(HueProperty, SaturationProperty, ValueProperty);
    }

    public SkinSaturationField()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var hue = SkinColorPalette.FromHsv(Hue, 1, 1, 255);
        var saturation = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Colors.White, 0), new GradientStop(hue, 1)]
        };
        var value = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 1)]
        };
        var rect = new Rect(Bounds.Size);
        context.FillRectangle(saturation, rect);
        context.FillRectangle(value, rect);

        var point = new Point(Saturation * Bounds.Width, (1 - Value) * Bounds.Height);
        context.DrawEllipse(null, new Pen(Brushes.Black, 3), point, 5, 5);
        context.DrawEllipse(null, new Pen(Brushes.White, 1.5), point, 5, 5);
    }

    private void Select(Point point)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        Saturation = Math.Clamp(point.X / Bounds.Width, 0, 1);
        Value = Math.Clamp(1 - point.Y / Bounds.Height, 0, 1);
        ColorPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _dragging = true;
        Select(e.GetPosition(this));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) Select(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }
}

/// <summary>
///     皮肤编辑器的调色盘：HSV 取色区 + 色相/不透明度滑条 + 常用色 + 最近使用。
///     从 WMCL 移植，去掉系统颜色对话框依赖，触摸屏可直接拖拽选色。
/// </summary>
public sealed class SkinColorPalette : UserControl
{
    public static readonly StyledProperty<Color> SelectedColorProperty =
        AvaloniaProperty.Register<SkinColorPalette, Color>(
            nameof(SelectedColor), Color.FromRgb(85, 85, 85));

    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private readonly SkinSaturationField _saturationField = new() { Height = 110 };
    private readonly MySlider _hueSlider = new() { MaxValue = 359 };
    private readonly MySlider _alphaSlider = new() { MaxValue = 255, Value = 255 };
    private readonly WrapPanel _commonColors = new() { ItemSpacing = 5, LineSpacing = 5 };
    private readonly WrapPanel _recentColors = new() { ItemSpacing = 5, LineSpacing = 5 };
    private readonly TextBlock _alphaLabel = new();
    private readonly TextBlock _hexLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _swatch = new() { Width = 32, Height = 24, CornerRadius = new CornerRadius(3) };
    private readonly List<Color> _recent = [];
    private bool _updating;

    public SkinColorPalette()
    {
        var content = new StackPanel { Spacing = 7, Margin = new Thickness(0, 4, 0, 0) };
        Content = content;

        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        heading.Children.Add(_swatch);
        Grid.SetColumn(_hexLabel, 1);
        heading.Children.Add(_hexLabel);
        content.Children.Add(heading);

        content.Children.Add(_saturationField);

        content.Children.Add(new TextBlock { Text = Lang.Text("Skin.Editor.Hue"), FontSize = 12 });
        content.Children.Add(new Border
        {
            Height = 20,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(Colors.Red, 0), new GradientStop(Colors.Yellow, 1d / 6),
                    new GradientStop(Colors.Lime, 2d / 6), new GradientStop(Colors.Cyan, .5),
                    new GradientStop(Colors.Blue, 4d / 6), new GradientStop(Colors.Magenta, 5d / 6),
                    new GradientStop(Colors.Red, 1)
                ]
            },
            ClipToBounds = true
        });
        content.Children.Add(_hueSlider);

        content.Children.Add(_alphaLabel);
        content.Children.Add(_alphaSlider);

        content.Children.Add(new TextBlock { Text = Lang.Text("Skin.Editor.CommonColors"), FontSize = 12 });
        content.Children.Add(_commonColors);
        content.Children.Add(new TextBlock { Text = Lang.Text("Skin.Editor.RecentColors"), FontSize = 12 });
        content.Children.Add(_recentColors);

        uint[] colors =
        [
            0xFF000000, 0xFF333333, 0xFF666666, 0xFF999999, 0xFFCCCCCC, 0xFFFFFFFF,
            0xFFE53935, 0xFFFF9800, 0xFFFFD600, 0xFF43A047, 0xFF00ACC1, 0xFF1E88E5,
            0xFF5E35B1, 0xFFEC407A, 0xFF795548, 0xFFFFE0BD, 0xFFEAC086, 0xFFBD8A60,
            0xFF8D5524, 0xFF5C3317, 0xFF252B38, 0xFFB1C7C5, 0xFFB03060, 0x00000000
        ];
        foreach (var argb in colors)
            _commonColors.Children.Add(ColorSwatch(Color.FromUInt32(argb), SelectColor));

        _saturationField.ColorPositionChanged += (_, _) => UpdateFromControls();
        _hueSlider.Change += (_, _) =>
        {
            if (_updating) return;
            _saturationField.Hue = _hueSlider.Value;
            UpdateFromControls();
        };
        _alphaSlider.Change += (_, _) =>
        {
            if (!_updating) UpdateFromControls();
        };

        SyncColor();
    }

    public void RememberColor(Color color)
    {
        _recent.Remove(color);
        _recent.Insert(0, color);
        if (_recent.Count > 8) _recent.RemoveAt(8);
        _recentColors.Children.Clear();
        foreach (var recent in _recent)
            _recentColors.Children.Add(ColorSwatch(recent, SelectColor));
    }

    private void SelectColor(Color color)
    {
        SetCurrentValue(SelectedColorProperty, color);
        RememberColor(color);
    }

    private static Border ColorSwatch(Color color, Action<Color> select)
    {
        var border = new Border
        {
            Width = 21,
            Height = 21,
            Margin = new Thickness(2),
            Background = new SolidColorBrush(color),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            CornerRadius = new CornerRadius(2)
        };
        ToolTip.SetTip(border, color.A == 0 ? Lang.Text("Skin.Editor.Transparent") : color.ToString());
        border.PointerPressed += (_, e) =>
        {
            select(color);
            e.Handled = true;
        };
        return border;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedColorProperty)
            SyncColor();
    }

    private void UpdateFromControls()
    {
        if (_updating) return;
        SetCurrentValue(
            SelectedColorProperty,
            FromHsv(_hueSlider.Value, _saturationField.Saturation, _saturationField.Value,
                (byte)Math.Clamp(_alphaSlider.Value, 0, 255)));
    }

    private void SyncColor()
    {
        _updating = true;
        var hsv = SelectedColor.ToHsv();
        if (hsv.S > 0.0001) _hueSlider.Value = (int)Math.Round(hsv.H);
        _saturationField.Hue = _hueSlider.Value;
        _saturationField.Saturation = hsv.S;
        _saturationField.Value = hsv.V;
        _alphaSlider.Value = SelectedColor.A;
        _alphaLabel.Text = string.Format(Lang.Text("Skin.Editor.Alpha"), SelectedColor.A * 100d / 255);
        _hexLabel.Text = SelectedColor.ToString();
        _swatch.Background = new SolidColorBrush(SelectedColor);
        _updating = false;
    }

    internal static Color FromHsv(double hue, double saturation, double value, byte alpha) =>
        new HsvColor(alpha / 255d, hue, Math.Clamp(saturation, 0, 1), Math.Clamp(value, 0, 1)).ToRgb();
}
