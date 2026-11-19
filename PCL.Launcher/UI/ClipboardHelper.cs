using System.Threading.Tasks;
using PCL.Core.Platform;

namespace PCL.UI;

/// <summary>
///     剪贴板统一入口：优先走平台注册的 IClipboardService（Android 用系统 ClipboardManager），
///     避免单视图模式下 Avalonia 的 TopLevel.Clipboard 拿不到宿主窗口而静默失效。
/// </summary>
public static class ClipboardHelper
{
    public static IClipboardService? Service => ApplicationRuntime.GetService<IClipboardService>();

    public static Task SetTextAsync(string? text)
    {
        return string.IsNullOrEmpty(text)
            ? Task.CompletedTask
            : Service?.SetTextAsync(text) ?? Task.CompletedTask;
    }

    public static async Task<string?> GetTextAsync()
    {
        return Service is null ? null : await Service.GetTextAsync().ConfigureAwait(false);
    }
}
