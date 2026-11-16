using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using PCL.Core.Logging;

namespace PCL.Views.Pages.Tools;

/// <summary>
///     把界面上的成就预览控件渲染成 PNG。控件本身负责排版与字体，
///     因此中文与自定义字体都能正确输出，不再依赖在线图片接口。
/// </summary>
public static class AchievementImageRenderer
{
    /// <summary>当前用于预览与导出的控件，由页面在初始化时注入。</summary>
    public static Control? Preview { get; set; }

    public static void Save(string path)
    {
        var control = Preview;
        if (control is null)
            throw new InvalidOperationException("成就预览控件尚未就绪");

        var width = Math.Max(1, (int)Math.Ceiling(control.Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(control.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(control);
        bitmap.Save(path);
        LogWrapper.Info("Tools", $"成就图片已导出：{path}（{width}x{height}）");
    }
}
