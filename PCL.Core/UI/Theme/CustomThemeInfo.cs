using System.Collections.Generic;
using System.Text.Json;

namespace PCL.Core.UI.Theme;

/// <summary>用户保存的自定义配色主题（三项微调参数的命名预设）。</summary>
public sealed record CustomThemeInfo(string Name, int Hue, int Light, int Chroma);

/// <summary>自定义主题的读取与保存。</summary>
public static class CustomThemeStore
{
    public static List<CustomThemeInfo> Load(string json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize<List<CustomThemeInfo>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Save(IEnumerable<CustomThemeInfo> themes) =>
        JsonSerializer.Serialize(themes);
}
