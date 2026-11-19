using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PCL.Core.Minecraft.Assets;
using PCL.Core.Tools.Lottery.Items;

namespace PCL.UI.Controls.Mc;

/// <summary>物品栏里的一格：稀有度与收集数量供悬停提示使用。</summary>
public sealed record InventorySlot(
    string Id,
    string Name,
    string IconPath,
    bool Owned,
    string RarityLabel = "",
    string RarityColorHex = "#FFFFFFFF",
    int Count = 0);

/// <summary>
///     按 Minecraft 创造模式物品栏还原的收藏库：面板、标签页、槽位、滚动条都取自客户端贴图，
///     物品图标按整数倍缩放绘制，未收集的物品盖一层暗色。
/// </summary>
public sealed class McCreativeInventory : Control
{
    private const int Columns = 9;
    private const int Rows = 5;
    private const int SlotPitch = 18;
    private const int SlotOriginX = 9;
    private const int SlotOriginY = 18;
    private const int TabSpriteWidth = 26;

    /// <summary>实时几何的参考画布尺寸（模型投影后的坐标系）。</summary>
    private const int IconReferenceSize = 32;

    /// <summary>标签栏右端对齐物品格右侧（9 格 × 18 + 左偏移 9）。</summary>
    private const int TabStripWidth = SlotOriginX + Columns * SlotPitch;

    /// <summary>标签页图标尺寸：原版就是 16×16，源图 64×64 正好 4:1 整数缩放。</summary>
    private const int TabIconSize = 16;

    /// <summary>每排标签数量固定为 5，标签宽度按标签栏宽度均分。</summary>
    private double TabWidth => TabStripWidth / (double)Math.Max(1,
        Math.Max(TopTabCount, TabCount - TopTabCount));
    private const int TabHeight = 32;

    /// <summary>标签页相对面板顶部上移的量；原版标签页底部会压住面板 4px。</summary>
    private const int TabOverlap = 4;

    private const int TabLift = TabHeight - TabOverlap;

    /// <summary>原版顶部标签页数量，其余分类排在面板下方。</summary>
    private const int TopTabCount = 5;
    private const int PanelWidth = 195;
    private const int PanelHeight = 136;

    public static readonly StyledProperty<IReadOnlyList<InventorySlot>?> ItemsProperty =
        AvaloniaProperty.Register<McCreativeInventory, IReadOnlyList<InventorySlot>?>(nameof(Items));

    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<McCreativeInventory, double>(
            nameof(Scale), 2, coerce: (_, value) =>
                double.IsFinite(value) ? Math.Clamp(value, 1, 4) : 2);

    private readonly Dictionary<string, Bitmap?> _iconCache = new(StringComparer.Ordinal);
    private Bitmap? _panel;
    private Bitmap? _scroller;
    private readonly Bitmap?[] _tabSelected = new Bitmap?[7];
    private readonly Bitmap?[] _tabUnselected = new Bitmap?[7];
    private readonly Bitmap?[] _bottomSelected = new Bitmap?[7];
    private readonly Bitmap?[] _bottomUnselected = new Bitmap?[7];
    private readonly List<string?> _tabIconPaths = [];
    private bool _assetsTried;

    private int _hoverSlot = -1;
    private Point _hoverPoint;

    public McCreativeInventory()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    /// <summary>按分类分组后的物品；每个分类一页。</summary>
    public IReadOnlyList<InventorySlot>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    /// <summary>当前标签页索引。</summary>
    public int SelectedTab { get; private set; }

    /// <summary>标签页数量。</summary>
    public int TabCount { get; private set; }

    /// <summary>当前页第一个物品的下标。</summary>
    public int PageOffset { get; private set; }

    /// <summary>鼠标在控件内的位置，供提示框定位。</summary>
    public Point HoveredPoint { get; private set; }

    /// <summary>当前鼠标停留的物品格，移出时为空。</summary>
    public InventorySlot? HoveredSlot { get; private set; }

    /// <summary>鼠标停留格变化时触发，参数为物品；移出时为空。</summary>
    public event EventHandler<InventorySlot?>? HoverChanged;

    /// <summary>点击物品格时触发，用于「选择物品」场景。</summary>
    public event EventHandler<InventorySlot>? ItemActivated;

    /// <summary>当前选中的物品 ID，用于「选择物品」场景的选中反馈。</summary>
    public string? SelectedId
    {
        get => _selectedId;
        set
        {
            if (string.Equals(_selectedId, value, StringComparison.Ordinal)) return;
            _selectedId = value;
            InvalidateVisual();
        }
    }

    private string? _selectedId;

    static McCreativeInventory()
    {
        AffectsRender<McCreativeInventory>(ItemsProperty, ScaleProperty);
    }

    /// <summary>
    ///     实际渲染倍率：同时受宽度与高度限制，避免只按宽度算导致面板被裁掉。
    /// </summary>
    private double RenderScale
    {
        get
        {
            var totalHeight = PanelHeight + TabLift + (TabCount > TopTabCount ? TabLift : 0);
            var byWidth = Bounds.Width / PanelWidth;
            var byHeight = Bounds.Height / totalHeight;
            var scale = Math.Min(byWidth, byHeight);
            return Math.Max(1, double.IsFinite(scale) && scale > 0 ? scale : 1);
        }
    }

    /// <summary>按可用宽度自动挑整数缩放倍率，保证整张面板放得下。</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var scale = Scale;
        if (double.IsFinite(availableSize.Width) && availableSize.Width > 0)
        {
            var fit = (int)Math.Floor(availableSize.Width / PanelWidth);
            scale = Math.Clamp(fit, 1, Scale);
        }

        if (double.IsFinite(availableSize.Height) && availableSize.Height > 0)
        {
            var extra = TabCount > TopTabCount ? TabLift : 0;
            var fit = (int)Math.Floor(availableSize.Height / (PanelHeight + TabLift + extra));
            scale = Math.Min(scale, Math.Clamp(fit, 1, Scale));
        }

        var tabsWidth = TabCount * TabWidth + 2;
        var hasBottom = TabCount > TopTabCount;
        var height = PanelHeight + TabLift + (hasBottom ? TabLift : 0);
        return new Size(Math.Max(PanelWidth, tabsWidth) * scale, height * scale);
    }

    /// <summary>贴图被重新提取后调用，下一次渲染重新加载。</summary>
    public void ReloadAssets()
    {
        _assetsTried = false;
        _panel = null;
        _scroller = null;
        Array.Clear(_tabSelected);
        Array.Clear(_tabUnselected);
        _iconCache.Clear();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void EnsureAssets()
    {
        if (_assetsTried) return;
        _assetsTried = true;
        try
        {
            _panel = Load(McAssetExtractor.CreativePanelPath);
            _scroller = Load(McAssetExtractor.CreativeScrollerPath);
            for (var i = 0; i < 7; i++)
            {
                _tabSelected[i] = Load(Path.Combine(McAssetExtractor.CreativeTabDir,
                    $"top_selected_{i + 1}.png"));
                _tabUnselected[i] = Load(Path.Combine(McAssetExtractor.CreativeTabDir,
                    $"top_unselected_{i + 1}.png"));
                _bottomSelected[i] = Load(Path.Combine(McAssetExtractor.CreativeTabDir,
                    $"bottom_selected_{i + 1}.png"));
                _bottomUnselected[i] = Load(Path.Combine(McAssetExtractor.CreativeTabDir,
                    $"bottom_unselected_{i + 1}.png"));
            }
        }
        catch (Exception)
        {
            // 贴图缺失时走纯色兜底，不影响使用。
        }
    }

    private static Bitmap? Load(string path)
    {
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public override void Render(DrawingContext context)
    {
        EnsureAssets();
        using var renderOptions = context.PushRenderOptions(
            new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None });
        var scale = RenderScale;
        var panelRect = new Rect(0, TabLift * scale, PanelWidth * scale, PanelHeight * scale);

        DrawTabs(context, scale);

        if (_panel is not null)
            context.DrawImage(_panel,
                new Rect(0, 0, PanelWidth, PanelHeight), panelRect);
        else
            context.FillRectangle(new SolidColorBrush(Color.Parse("#FFC6C6C6")), panelRect);

        DrawSlots(context, scale);
        DrawScroller(context, scale);
    }

    private void DrawTabs(DrawingContext context, double scale)
    {
        var topCount = Math.Min(TopTabCount, TabCount);
        for (var i = 0; i < topCount; i++)
            DrawTab(context, scale, i, topCount, true);

        var bottomCount = Math.Max(0, TabCount - TopTabCount);
        for (var i = 0; i < bottomCount; i++)
            DrawTab(context, scale, TopTabCount + i, bottomCount, false);
    }

    /// <summary>标签页底图形状：首尾用边角形状，中间统一用中间形状。</summary>
    private static int TabShapeIndex(int indexInRow, int rowCount)
    {
        if (rowCount <= 1) return 0;
        if (indexInRow == 0) return 0;
        if (indexInRow == rowCount - 1) return 6;
        return 1;
    }

    /// <summary>三段式拉伸：左右各 8px 保持原样，中间拉宽，避免像素画被整体拉变形。</summary>
    private static void DrawSliced(DrawingContext context, Bitmap bitmap, Rect target, double scale)
    {
        const int cap = 8;
        var source = new Rect(0, 0, TabSpriteWidth, TabHeight);
        if (target.Width <= cap * 2 * scale)
        {
            context.DrawImage(bitmap, source, target);
            return;
        }

        context.DrawImage(bitmap, new Rect(0, 0, cap, TabHeight),
            new Rect(target.X, target.Y, cap * scale, target.Height));
        context.DrawImage(bitmap, new Rect(cap, 0, TabSpriteWidth - cap * 2, TabHeight),
            new Rect(target.X + cap * scale, target.Y, target.Width - cap * 2 * scale, target.Height));
        context.DrawImage(bitmap, new Rect(TabSpriteWidth - cap, 0, cap, TabHeight),
            new Rect(target.Right - cap * scale, target.Y, cap * scale, target.Height));
    }

    private static Rect FullRect(Bitmap bitmap) =>
        new(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);

    private void DrawTab(DrawingContext context, double scale, int index, int rowCount, bool top)
    {
        var shape = TabShapeIndex(index - (top ? 0 : TopTabCount), rowCount);
        var bitmap = top
            ? index == SelectedTab ? _tabSelected[shape] : _tabUnselected[shape]
            : index == SelectedTab ? _bottomSelected[shape] : _bottomUnselected[shape];

        var slot = index - (top ? 0 : TopTabCount);
        var x = Math.Round(slot * TabWidth) * scale;
        var width = Math.Round((slot + 1) * TabWidth) * scale - x;
        var y = top
            ? 0
            : (TabLift + PanelHeight - TabOverlap) * scale;
        var rect = new Rect(x, y, width, TabHeight * scale);
        if (bitmap is not null)
            DrawSliced(context, bitmap, rect, scale);
        else
            context.FillRectangle(
                new SolidColorBrush(Color.Parse(index == SelectedTab ? "#FF8B8B8B" : "#FF6E6E6E")), rect);

        var iconPath = index < _tabIconPaths.Count ? _tabIconPaths[index] : null;
        if (string.IsNullOrWhiteSpace(iconPath) || LoadIcon(iconPath) is not { } icon) return;
        var iconOffsetY = (TabHeight - TabIconSize) / 2d;
        var iconTop = top
            ? iconOffsetY * scale
            : (TabLift + PanelHeight - TabOverlap + iconOffsetY) * scale;
        var iconLeft = x + (width / scale - TabIconSize) / 2d * scale;
        context.DrawImage(icon, FullRect(icon),
            new Rect(iconLeft, iconTop, TabIconSize * scale, TabIconSize * scale));
    }

    /// <summary>实时几何缓存：物品 ID → 四边形列表（构建失败缓存 null）。</summary>
    private readonly Dictionary<string, IReadOnlyList<ItemQuad>?> _quads = [];

    /// <summary>Skia 贴图 → Avalonia 位图缓存。</summary>
    private readonly Dictionary<object, Bitmap?> _textures = [];

    /// <summary>把模型四边形直接光栅化到格子，任意缩放倍率都清晰。</summary>
    /// <summary>应急开关：设置 PCL_NO_REALTIME_ICONS=1 可退回预渲染图标。</summary>
    /// <summary>默认走预渲染 PNG（实时光栅化的面序仍有问题，用环境变量可重新启用）。</summary>
    private static readonly bool RealtimeDisabled =
        Environment.GetEnvironmentVariable("PCL_REALTIME_ICONS") != "1";

    private bool TryDrawQuads(DrawingContext context, string id, Rect slotRect)
    {
        if (RealtimeDisabled) return false;

        if (!_quads.TryGetValue(id, out var quads))
        {
            try
            {
                quads = MinecraftItemCatalog.BuildQuadsFor(id, IconReferenceSize);
            }
            catch (Exception)
            {
                quads = null;
            }

            _quads[id] = quads;
        }

        if (quads is null || quads.Count == 0) return false;

        var ratio = slotRect.Width / IconReferenceSize;
        foreach (var quad in quads)
        {
            var texture = ResolveTexture(quad.Texture);
            if (texture is null) continue;

            var points = quad.Points;
            var p0 = new Point(slotRect.X + points[0].X * ratio, slotRect.Y + points[0].Y * ratio);
            var p1 = new Point(slotRect.X + points[1].X * ratio, slotRect.Y + points[1].Y * ratio);
            var p2 = new Point(slotRect.X + points[2].X * ratio, slotRect.Y + points[2].Y * ratio);
            var p3 = new Point(slotRect.X + points[3].X * ratio, slotRect.Y + points[3].Y * ratio);

            // 贴图源矩形 → 目标平行四边形 的仿射变换。
            var source = quad.Source;
            if (source.Width <= 0 || source.Height <= 0) continue;
            var a = (p1.X - p0.X) / source.Width;
            var b = (p1.Y - p0.Y) / source.Width;
            var c2 = (p3.X - p0.X) / source.Height;
            var d = (p3.Y - p0.Y) / source.Height;
            var e = p0.X - a * source.Left - c2 * source.Top;
            var f = p0.Y - b * source.Left - d * source.Top;
            if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c2) ||
                !double.IsFinite(d) || Math.Abs(a * d - b * c2) < 1e-9)
                continue;

            var matrix = new Matrix(a, b, c2, d, e, f);

            var clip = new StreamGeometry();
            using (var geometry = clip.Open())
            {
                geometry.BeginFigure(p0, true);
                geometry.LineTo(p1);
                geometry.LineTo(p2);
                geometry.LineTo(p3);
                geometry.EndFigure(true);
            }

            // 先裁剪再变换：裁剪几何用的是控件坐标，放在变换之后会被二次变换。
            using (context.PushGeometryClip(clip))
            using (context.PushTransform(matrix))
            {
                context.DrawImage(texture, new Rect(0, 0, texture.PixelSize.Width, texture.PixelSize.Height));
                // 用黑色叠加还原原版的明暗（贴图本身是满亮度）。
                var shade = 1d - (quad.Color.Red / 255d);
                if (shade > 0.02)
                    context.FillRectangle(
                        new SolidColorBrush(Color.FromArgb((byte)(shade * 255), 0, 0, 0)),
                        new Rect(0, 0, texture.PixelSize.Width, texture.PixelSize.Height));
            }
        }

        return true;
    }

    private Bitmap? ResolveTexture(SkiaSharp.SKBitmap bitmap)
    {
        if (_textures.TryGetValue(bitmap, out var cached)) return cached;
        Bitmap? result = null;
        try
        {
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            if (data is not null)
            {
                using var stream = new MemoryStream(data.ToArray());
                result = new Bitmap(stream);
            }
        }
        catch (Exception)
        {
            result = null;
        }

        _textures[bitmap] = result;
        return result;
    }

    private void DrawSlots(DrawingContext context, double scale)
    {
        var items = CurrentPageItems();
        var slots = Columns * Rows;

        for (var index = 0; index < slots; index++)
        {
            var column = index % Columns;
            var row = index / Columns;
            var slotRect = new Rect(
                (SlotOriginX + column * SlotPitch) * scale,
                (TabLift + SlotOriginY + row * SlotPitch) * scale,
                16 * scale, 16 * scale);

            if (index < items.Count)
            {
                var slot = items[index];

                // 原版手感：悬停的格子把物品略微抬起并放大一点。
                if (index == _hoverSlot)
                    slotRect = slotRect.Translate(new Vector(0, -1 * scale)).Inflate(1 * scale);

                var drawn = TryDrawQuads(context, slot.Id, slotRect);
                if (!drawn && LoadIcon(slot.IconPath) is { } icon)
                {
                    context.DrawImage(icon, FullRect(icon), slotRect);
                    drawn = true;
                }

                if (drawn && !slot.Owned)
                    context.FillRectangle(new SolidColorBrush(Color.Parse("#88000000")), slotRect);
            }

            var selected = _selectedId is { Length: > 0 } && index < items.Count &&
                           string.Equals(items[index].Id, _selectedId, StringComparison.Ordinal);

            if (index == _hoverSlot && !selected)
                context.FillRectangle(new SolidColorBrush(Color.Parse("#66FFFFFF")), slotRect);

            if (selected)
            {
                // 选中反馈：整体提亮 + 白色描边，和悬停区分开。
                context.FillRectangle(new SolidColorBrush(Color.Parse("#66FFFFFF")), slotRect);
                var pen = new Pen(new SolidColorBrush(Color.Parse("#FFFFFFFF")), Math.Max(1, scale));
                context.DrawRectangle(null, pen, slotRect.Deflate(pen.Thickness / 2));
            }
        }
    }

    /// <summary>滚动条滑块高度固定为两格，位置按偏移比例映射，避免不同页长度不一致。</summary>
    private double ScrollerThumbHeight(double scale) => SlotPitch * 2 * scale;

    private (double Top, double Height, double TrackTop, double TrackHeight) ScrollerGeometry(double scale)
    {
        var total = TabCount == 0 ? 0 : TabItems(SelectedTab).Count;
        var trackTop = (TabLift + SlotOriginY) * scale;
        // 滚动条凹槽比物品格区域略长，滑块行程按凹槽高度算才能滑到底。
        var trackHeight = (PanelHeight - SlotOriginY - 19) * scale;
        var visible = Columns * Rows;
        var thumbHeight = total <= visible ? trackHeight : Math.Min(trackHeight, ScrollerThumbHeight(scale));
        var maxOffset = Math.Max(0, total - visible);
        var ratio = maxOffset == 0 ? 0d : Math.Min(1d, PageOffset / (double)maxOffset);
        var thumbTop = trackTop + (trackHeight - thumbHeight) * ratio;
        return (thumbTop, thumbHeight, trackTop, trackHeight);
    }

    private void DrawScroller(DrawingContext context, double scale)
    {
        if (_scroller is null) return;
        var (thumbTop, thumbHeight, _, _) = ScrollerGeometry(scale);
        context.DrawImage(_scroller, new Rect(0, 0, 12, 15),
            new Rect((PanelWidth - 20) * scale, thumbTop, 12 * scale, thumbHeight));
    }

    /// <summary>把滚动条上的位置换算成物品偏移量。</summary>
    private void ScrollToPointer(double y, double scale)
    {
        var (_, thumbHeight, trackTop, trackHeight) = ScrollerGeometry(scale);
        var total = TabItems(SelectedTab).Count;
        var maxOffset = Math.Max(0, total - Columns * Rows);
        if (maxOffset == 0) return;

        var usable = Math.Max(1d, trackHeight - thumbHeight);
        var ratio = Math.Clamp((y - trackTop - thumbHeight / 2d) / usable, 0d, 1d);
        var offset = (int)Math.Round(ratio * maxOffset / Columns) * Columns;
        offset = Math.Clamp(offset, 0, maxOffset);
        if (offset == PageOffset) return;
        PageOffset = offset;
        _hoverSlot = -1;
        InvalidateVisual();
    }

    private readonly List<InventorySlot> _flat = [];

    /// <summary>外部在设置 Items 前调用，告诉控件有哪些分类以及每类多少物品。</summary>
    public void SetLayout(IReadOnlyList<int> tabCounts, IReadOnlyList<string?>? tabIconPaths = null)
    {
        TabCount = tabCounts.Count;
        _tabCounts.Clear();
        _tabCounts.AddRange(tabCounts);
        _tabIconPaths.Clear();
        if (tabIconPaths is not null) _tabIconPaths.AddRange(tabIconPaths);
        SelectedTab = 0;
        PageOffset = 0;
        InvalidateVisual();
    }

    private readonly List<int> _tabCounts = [];

    private IReadOnlyList<InventorySlot> TabItems(int tab)
    {
        if (Items is null) return [];
        var start = 0;
        for (var i = 0; i < tab && i < _tabCounts.Count; i++) start += _tabCounts[i];
        var count = tab < _tabCounts.Count ? _tabCounts[tab] : 0;
        if (start >= Items.Count) return [];
        count = Math.Min(count, Items.Count - start);
        var result = new List<InventorySlot>(count);
        for (var i = 0; i < count; i++) result.Add(Items[start + i]);
        return result;
    }

    private InventorySlot? FirstItemOfTab(int tab)
    {
        var list = TabItems(tab);
        return list.Count > 0 ? list[0] : null;
    }

    private List<InventorySlot> CurrentPageItems()
    {
        var all = TabItems(SelectedTab);
        var result = new List<InventorySlot>(Columns * Rows);
        for (var i = 0; i < Columns * Rows && PageOffset + i < all.Count; i++)
            result.Add(all[PageOffset + i]);
        return result;
    }

    private Bitmap? LoadIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_iconCache.TryGetValue(path, out var cached)) return cached;
        var bitmap = Load(path);
        _iconCache[path] = bitmap;
        return bitmap;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);

        if (_dragging)
        {
            var scaleNow = RenderScale;
            var scrollerLeft = (PanelWidth - 20) * scaleNow;
            if (position.X >= scrollerLeft)
            {
                ScrollToPointer(position.Y, scaleNow);
                _dragMoved = true;
                e.Handled = true;
                return;
            }

            var rowsMoved = (int)((_dragStartY - position.Y) / (SlotPitch * scaleNow));
            if (rowsMoved != 0)
            {
                _dragMoved = true;
                var total = TabItems(SelectedTab).Count;
                var maxOffset = Math.Max(0, total - Columns * Rows);
                var next = Math.Clamp(_dragStartOffset + rowsMoved * Columns, 0, maxOffset);
                if (next != PageOffset)
                {
                    PageOffset = next;
                    _hoverSlot = -1;
                    InvalidateVisual();
                }
            }

            e.Handled = true;
            return;
        }

        _hoverPoint = position;
        HoveredPoint = position;
        var slot = HitTestSlot(position);
        if (slot == _hoverSlot) return;
        _hoverSlot = slot;
        var items = CurrentPageItems();
        HoveredSlot = slot >= 0 && slot < items.Count ? items[slot] : null;
        HoverChanged?.Invoke(this, HoveredSlot);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverSlot < 0) return;
        _hoverSlot = -1;
        HoveredSlot = null;
        HoverChanged?.Invoke(this, null);
        InvalidateVisual();
    }

    private bool _dragging;
    private double _dragStartY;
    private int _dragStartOffset;
    private bool _dragMoved;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var position = e.GetPosition(this);
        var scale = RenderScale;

        // 触摸屏没有滚轮：在物品区按住拖动即滚动，拖动距离超过一格时不触发点击。
        var scrollerLeft = (PanelWidth - 20) * scale;
        if (position.X >= scrollerLeft && position.Y >= (TabLift + SlotOriginY) * scale)
        {
            ScrollToPointer(position.Y, scale);
            _dragging = true;
            _dragMoved = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var bottomTop = (TabLift + PanelHeight - TabOverlap) * scale;
        var inTop = position.Y < TabLift * scale;
        var inBottom = TabCount > TopTabCount && position.Y >= bottomTop;
        if (inTop || inBottom)
        {
            var local = (int)Math.Floor(position.X / scale / TabWidth);
            var tab = inTop ? local : TopTabCount + local;
            var limit = inTop ? Math.Min(TopTabCount, TabCount) : TabCount - TopTabCount;
            if (local < 0 || local >= limit) return;
            if (tab >= 0 && tab < TabCount && tab != SelectedTab)
            {
                SelectedTab = tab;
                PageOffset = 0;
                _hoverSlot = -1;
                InvalidateVisual();
                HoverChanged?.Invoke(this, null);
            }
            e.Handled = true;
            return;
        }

        _dragging = true;
        _dragMoved = false;
        _dragStartY = position.Y;
        _dragStartOffset = PageOffset;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var wasDragging = _dragging;
        var moved = _dragMoved;
        _dragging = false;
        _dragMoved = false;
        e.Pointer.Capture(null);

        if (!wasDragging || moved) return;

        var slot = HitTestSlot(e.GetPosition(this));
        if (slot < 0) return;
        var items = CurrentPageItems();
        if (slot < items.Count) ItemActivated?.Invoke(this, items[slot]);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var total = TabItems(SelectedTab).Count;
        var maxOffset = Math.Max(0, total - Columns * Rows);
        var next = Math.Clamp(PageOffset + (e.Delta.Y > 0 ? -Columns : Columns), 0, maxOffset);
        if (next == PageOffset) return;
        PageOffset = next;
        _hoverSlot = -1;
        InvalidateVisual();
        e.Handled = true;
    }

    private int HitTestSlot(Point position)
    {
        var scale = RenderScale;
        var localX = position.X / scale - SlotOriginX;
        var localY = position.Y / scale - TabLift - SlotOriginY;
        if (localX < 0 || localY < 0) return -1;
        var column = (int)(localX / SlotPitch);
        var row = (int)(localY / SlotPitch);
        if (column >= Columns || row >= Rows) return -1;
        if (localX - column * SlotPitch > 16 || localY - row * SlotPitch > 16) return -1;
        return row * Columns + column;
    }
}
