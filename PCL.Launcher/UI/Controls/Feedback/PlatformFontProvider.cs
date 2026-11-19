using Avalonia.Media;
using PCL.Core.Platform;

namespace PCL.UI.Controls;

internal static class PlatformFontProvider
{
    public static IPlatformFontProvider Current => PlatformRuntime.Current.Kind switch
    {
        PlatformKind.Windows => new WindowsFontProvider(),
        PlatformKind.Linux => new LinuxFontProvider(),
        PlatformKind.MacOs => new MacOsFontProvider(),
        // Android 没有 Bahnschrift 等系统字体，使用内嵌字体链。
        _ => new LinuxFontProvider()
    };
}

internal sealed class WindowsFontProvider : IPlatformFontProvider
{
    public FontFamily CreateBannerFont() => LaunchFonts.Build("Bahnschrift");
}

internal sealed class LinuxFontProvider : IPlatformFontProvider
{
    public FontFamily CreateBannerFont() => new(LaunchFonts.EmbeddedFontUri);
}

internal sealed class MacOsFontProvider : IPlatformFontProvider
{
    public FontFamily CreateBannerFont() => new(LaunchFonts.EmbeddedFontUri);
}
