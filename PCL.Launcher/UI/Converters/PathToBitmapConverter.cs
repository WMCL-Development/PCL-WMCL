using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace PCL.UI.Converters;

/// <summary>把本地图片路径转成位图，供物品图标等绑定使用；同一路径只解码一次。</summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    public static PathToBitmapConverter Instance { get; } = new();

    private readonly ConcurrentDictionary<string, Bitmap?> _cache = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        return _cache.GetOrAdd(path, static p =>
        {
            try
            {
                return new Bitmap(p);
            }
            catch (Exception)
            {
                return null;
            }
        });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
