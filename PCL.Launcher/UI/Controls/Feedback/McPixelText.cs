using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PCL.Core.Logging;
using PCL.Core.Minecraft.Assets;

namespace PCL.UI.Controls;

/// <summary>
///     用 Minecraft 自带的点阵字体绘制文字：拉丁字符取客户端里的 ascii.png（8×8），
///     其余字符取 GNU Unifont 点阵（16×16）。两个资源都从用户本机客户端提取，不随程序分发。
/// </summary>
public class McPixelText : Control
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<McPixelText, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<double> PixelScaleProperty =
        AvaloniaProperty.Register<McPixelText, double>(nameof(PixelScale), 2d);

    /// <summary>是否绘制 1 像素深色投影（原版物品名的描边效果）。</summary>
    public static readonly StyledProperty<bool> ShadowProperty =
        AvaloniaProperty.Register<McPixelText, bool>(nameof(Shadow));

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<McPixelText, IBrush>(nameof(Foreground), Brushes.White);

    private static byte[]? _asciiPixels;
    private static int _asciiWidth;
    private static int _asciiHeight;
    private static Dictionary<int, ushort[]>? _unifont;

    public McPixelText()
    {
        AffectsRender<McPixelText>(TextProperty, PixelScaleProperty, ForegroundProperty);
        EnsureAssets();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double PixelScale
    {
        get => GetValue(PixelScaleProperty);
        set => SetValue(PixelScaleProperty, value);
    }

    public bool Shadow
    {
        get => GetValue(ShadowProperty);
        set => SetValue(ShadowProperty, value);
    }

    public IBrush Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>读取并缓存点阵资源；缺失时静默降级为空绘制。</summary>
    private static void EnsureAssets()
    {
        // 资源可能在运行期才下载完，缺哪份就补哪份。
        if (_asciiPixels is not null && _unifont is not null) return;
        if (_asciiPixels is null && !File.Exists(McAssetExtractor.FontTexturePath)) return;

        try
        {
            var asciiPath = McAssetExtractor.FontTexturePath;
            if (File.Exists(asciiPath))
            {
                using var stream = File.OpenRead(asciiPath);
                var bitmap = new Bitmap(stream);
                _asciiWidth = bitmap.PixelSize.Width;
                _asciiHeight = bitmap.PixelSize.Height;
                _asciiPixels = new byte[_asciiWidth * _asciiHeight * 4];
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                    _asciiPixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    bitmap.CopyPixels(
                        new PixelRect(0, 0, _asciiWidth, _asciiHeight),
                        handle.AddrOfPinnedObject(),
                        _asciiPixels.Length,
                        _asciiWidth * 4);
                }
                finally
                {
                    handle.Free();
                }
            }

            var hexPath = McAssetExtractor.UnifontHexPath;
            if (_unifont is null && File.Exists(hexPath)) _unifont = LoadUnifont(hexPath);


        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Font", "加载点阵字体失败：" + exception.Message);
        }
    }

    private static Dictionary<int, ushort[]> LoadUnifont(string path)
    {
        var result = new Dictionary<int, ushort[]>(70000);
        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;

            if (!int.TryParse(line[..separator], System.Globalization.NumberStyles.HexNumber,
                    null, out var codePoint))
                continue;

            var data = line[(separator + 1)..].Trim();
            // Unifont 每行是 16 位：8 像素宽用 2 个十六进制字符，16 像素宽用 4 个。
            var hexPerRow = data.Length / 16;
            if (hexPerRow is not (2 or 4)) continue;

            var rows = new ushort[16];
            for (var row = 0; row < 16; row++)
            {
                var value = Convert.ToUInt16(data.Substring(row * hexPerRow, hexPerRow), 16);
                rows[row] = (ushort)(hexPerRow == 2 ? value << 8 : value);
            }

            result[codePoint] = rows;
        }

        return result;
    }

    public override void Render(DrawingContext context)
    {
        EnsureAssets();

        var text = Text;
        if (string.IsNullOrEmpty(text)) return;

        var scale = Math.Max(1d, PixelScale);
        if (Shadow)
        {
            using (context.PushTransform(Avalonia.Matrix.CreateTranslation(scale * 0.5, scale * 0.5)))
                DrawText(context, text, scale, ShadowBrush);
        }

        DrawText(context, text, scale, Foreground);
    }

    private static readonly IBrush ShadowBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF202020"));

    private void DrawText(DrawingContext context, string text, double scale, IBrush brush)
    {
        var cursorX = 0d;
        var lineHeight = 16 * scale;

        foreach (var rune in text.EnumerateRunes())
        {
            var codePoint = rune.Value;

            // 拉丁字符优先用 8×8 位图，其余走 Unifont 16×16。
            if (codePoint is >= 32 and <= 126 && _asciiPixels is not null)
            {
                DrawAscii(context, codePoint, cursorX, scale, brush);
                cursorX += 8 * scale;
                continue;
            }

            if (_unifont is not null && _unifont.TryGetValue(codePoint, out var glyph))
            {
                // Unifont 是 16×16 点阵，按半尺寸绘制才能和 8×8 的拉丁字形同高。
                DrawUnifont(context, glyph, cursorX, scale * 0.5, brush);
                cursorX += 8 * scale;
                continue;
            }

            // 没有点阵数据时按 8 像素宽留白，避免文字挤在一起。
            cursorX += 8 * scale;
        }

        _ = lineHeight;
    }

    private void DrawAscii(DrawingContext context, int codePoint, double originX, double scale, IBrush brush)
    {
        const int glyphSize = 8;
        const int columns = 16;
        var index = codePoint - 32;
        var glyphX = index % columns * glyphSize;
        var glyphY = index / columns * glyphSize;

        for (var row = 0; row < glyphSize; row++)
        {
            for (var column = 0; column < glyphSize; column++)
            {
                var pixelX = glyphX + column;
                var pixelY = glyphY + row;
                if (pixelX >= _asciiWidth || pixelY >= _asciiHeight) continue;

                var offset = (pixelY * _asciiWidth + pixelX) * 4;
                if (_asciiPixels![offset + 3] < 128) continue;

                context.FillRectangle(brush, new Rect(
                    originX + column * scale,
                    row * scale,
                    scale,
                    scale));
            }
        }
    }

    private void DrawUnifont(DrawingContext context, ushort[] glyph, double originX, double scale, IBrush brush)
    {
        for (var row = 0; row < 16; row++)
        {
            var bits = glyph[row];
            for (var column = 0; column < 16; column++)
            {
                if ((bits & (1 << (15 - column))) == 0) continue;
                context.FillRectangle(brush, new Rect(
                    originX + column * scale,
                    row * scale,
                    scale,
                    scale));
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text ?? string.Empty;
        var scale = Math.Max(1d, PixelScale);
        var width = 0d;
        foreach (var rune in text.EnumerateRunes()) width += 8 * scale;
        return new Size(width, 8 * scale);
    }
}
