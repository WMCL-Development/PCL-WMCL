using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCL.Core.App;
using SkiaSharp;

namespace PCL.Core.Tools.Lottery.Items;

/// <summary>
/// 统一把物品图标规格化为 16×16 的 PNG（游戏内 GUI 尺寸，避免二次缩放）。
/// 32×32/16×16 的原始图标先在这里用最近邻放大，界面按原尺寸显示时就不会再被插值糊掉。
/// </summary>
internal static class ItemIconWriter
{
    /// <summary>图标输出尺寸：32×32，与 2 倍物品栏图标显示尺寸一致。</summary>
    public const int IconSize = 32;

    public static string? SaveNormalized(Stream source, string ns, string id, int size = IconSize)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(source);
            if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0) return null;

            using var surface = SKSurface.Create(
                new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (surface is null) return null;
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(
                image,
                new SKRect(0, 0, size, size),
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));

            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            if (data is null) return null;

            var directory = Path.Combine(Paths.SharedLocalData, "lottery", "item-icons", "v2", size.ToString());
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, SafeName(ns) + "_" + SafeName(id) + ".png");
            using var output = File.Create(target);
            data.SaveTo(output);
            return target;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private static string SafeName(string value) =>
        string.Concat(value.Select(c =>
            char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
}
