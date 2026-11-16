using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Core.App.Localization;
using PCL.Core.ViewModel;

namespace PCL.UI.Controls.Skin;

/// <summary>皮肤像素编辑器：管理 64×32 / 64×64 皮肤的像素数据、颜色与绘制操作。</summary>
public partial class SkinEditorViewModel : ViewModelBase
{
    private const int MaxUndoDepth = 200;

    private readonly Stack<Color?[]> _undoStack = [];
    private readonly Stack<Color?[]> _redoStack = [];

    [ObservableProperty] public partial bool HasSkin { get; set; }

    [ObservableProperty] public partial string Status { get; set; } = string.Empty;

    [ObservableProperty] public partial bool IsEraser { get; set; }

    [ObservableProperty] public partial int BrushSize { get; set; } = 1;

    [ObservableProperty] public partial Color SelectedColor { get; set; } = Color.Parse("#FF555555");

    public int LastX { get; set; } = -1;

    public int LastY { get; set; } = -1;

    /// <summary>皮肤像素数据，行优先排列。null 表示透明。</summary>
    public Color?[] Pixels { get; private set; } = [];

    public int PixelsVersion { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    private void NotifyPixelsChanged()
    {
        PixelsVersion++;
        OnPropertyChanged(nameof(PixelsVersion));
    }

    /// <summary>从位图加载皮肤。</summary>
    public void LoadSkin(Bitmap? skin)
    {
        if (skin is null)
        {
            HasSkin = false;
            Pixels = [];
            Width = 0;
            Height = 0;
            LastX = -1;
            LastY = -1;
            Status = Lang.Text("Skin.Editor.NoSkin");
            ClearHistory();
            NotifyPixelsChanged();
            return;
        }

        Width = skin.PixelSize.Width;
        Height = skin.PixelSize.Height;
        Pixels = new Color?[Width * Height];
        LastX = -1;
        LastY = -1;
        using var pixels = new WriteableBitmap(skin.PixelSize, skin.Dpi,
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = pixels.Lock();
        skin.CopyPixels(buffer);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var idx = y * buffer.RowBytes + x * 4;
                var b = Marshal.ReadByte(buffer.Address, idx);
                var g = Marshal.ReadByte(buffer.Address, idx + 1);
                var r = Marshal.ReadByte(buffer.Address, idx + 2);
                var a = Marshal.ReadByte(buffer.Address, idx + 3);
                Pixels[y * Width + x] = a > 0 ? Color.FromArgb(a, r, g, b) : null;
            }
        }

        HasSkin = true;
        Status = Lang.Text("Skin.Editor.Loaded", Width, Height);
        ClearHistory();
        NotifyPixelsChanged();
    }

    /// <summary>把当前像素数据编码为位图。</summary>
    public Bitmap? ToBitmap()
    {
        if (!HasSkin || Width <= 0 || Height <= 0) return null;
        var result = new WriteableBitmap(
            new PixelSize(Width, Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = result.Lock();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var idx = y * buffer.RowBytes + x * 4;
                var color = Pixels[y * Width + x];
                Marshal.WriteByte(buffer.Address, idx, color?.B ?? 0);
                Marshal.WriteByte(buffer.Address, idx + 1, color?.G ?? 0);
                Marshal.WriteByte(buffer.Address, idx + 2, color?.R ?? 0);
                Marshal.WriteByte(buffer.Address, idx + 3, color?.A ?? 0);
            }
        }

        return result;
    }

    public void PaintPixel(int x, int y)
    {
        if (!HasSkin || x < 0 || y < 0 || x >= Width || y >= Height) return;
        LastX = x;
        LastY = y;
        var size = Math.Clamp(BrushSize, 1, 4);
        var changed = false;
        for (var dy = 0; dy < size; dy++)
            for (var dx = 0; dx < size; dx++)
                changed |= SetPixel(x + dx, y + dy);
        if (changed) NotifyPixelsChanged();
    }

    public void FillAtLast()
    {
        if (!HasSkin) return;
        Fill(LastX >= 0 ? LastX : Width / 2, LastY >= 0 ? LastY : Height / 2);
    }

    private bool SetPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
        var idx = y * Width + x;
        var next = IsEraser ? (Color?)null : SelectedColor;
        if (Pixels[idx] == next) return false;
        Pixels[idx] = next;
        return true;
    }

    public void Fill(int x, int y)
    {
        if (!HasSkin || x < 0 || y < 0 || x >= Width || y >= Height) return;
        var target = Pixels[y * Width + x];
        var fillColor = IsEraser ? (Color?)null : SelectedColor;
        if (target == fillColor) return;

        SaveState();
        var stack = new Stack<(int, int)>();
        stack.Push((x, y));
        var visited = new HashSet<(int, int)>();
        var filled = 0;
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) continue;
            if (!visited.Add((cx, cy))) continue;
            var idx = cy * Width + cx;
            if (Pixels[idx] != target) continue;
            Pixels[idx] = fillColor;
            filled++;
            stack.Push((cx + 1, cy));
            stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1));
            stack.Push((cx, cy - 1));
        }

        Status = Lang.Text("Skin.Editor.Filled", filled);
        NotifyPixelsChanged();
    }

    public void MirrorLeftToRight()
    {
        if (!HasSkin) return;
        SaveState();
        var half = Width / 2;
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < half; x++)
                Pixels[y * Width + (Width - 1 - x)] = Pixels[y * Width + x];
        Status = Lang.Text("Skin.Editor.MirroredH");
        NotifyPixelsChanged();
    }

    public void MirrorTopToBottom()
    {
        if (!HasSkin) return;
        SaveState();
        var half = Height / 2;
        for (var x = 0; x < Width; x++)
            for (var y = 0; y < half; y++)
                Pixels[(Height - 1 - y) * Width + x] = Pixels[y * Width + x];
        Status = Lang.Text("Skin.Editor.MirroredV");
        NotifyPixelsChanged();
    }

    public void Clear()
    {
        if (!HasSkin) return;
        SaveState();
        for (var i = 0; i < Pixels.Length; i++) Pixels[i] = null;
        Status = Lang.Text("Skin.Editor.Cleared");
        NotifyPixelsChanged();
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push([.. Pixels]);
        Pixels = _undoStack.Pop();
        Status = Lang.Text("Skin.Editor.Undone");
        NotifyPixelsChanged();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push([.. Pixels]);
        Pixels = _redoStack.Pop();
        Status = Lang.Text("Skin.Editor.Redone");
        NotifyPixelsChanged();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void BeginStroke() => SaveState();

    [RelayCommand]
    private void ToggleEraser()
    {
        IsEraser = !IsEraser;
        Status = Lang.Text(IsEraser ? "Skin.Editor.EraserOn" : "Skin.Editor.EraserOff");
    }

    private void SaveState()
    {
        while (_undoStack.Count >= MaxUndoDepth) _undoStack.Pop();
        _undoStack.Push([.. Pixels]);
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void ClearHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
