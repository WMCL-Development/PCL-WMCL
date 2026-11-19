using System;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Platform;

namespace PCL.Platform.Android;

/// <summary>
///     由 App 注入的 SAF 文件选择实现。
///     PCL.Platform.Android 是类库，拿不到 Activity 的 onActivityResult，
///     所以真正的实现由 <c>PCL.Launcher.Android</c> 在 Activity 创建时挂进来。
/// </summary>
public static class AndroidFilePickerBridge
{
    /// <summary>选择文件：参数为是否多选，返回本机可读的临时路径。</summary>
    public static Func<bool, Task<string[]>>? PickFiles;

    /// <summary>保存文件：参数为建议文件名，返回可写的临时路径（写入后会由 App 复制回用户选择的位置）。</summary>
    public static Func<string, Task<string>>? SaveFile;

    /// <summary>选择文件夹：返回可访问的目录路径，无法映射时返回空字符串。</summary>
    public static Func<Task<string>>? PickFolder;

    /// <summary>当前是否在 Android 主线程；同步 API 在主线程上不能阻塞。</summary>
    public static Func<bool>? IsMainThread;
}

/// <summary>Android 文件对话框：走系统 SAF 选择器，选择结果由 App 侧转换成临时文件路径。</summary>
public sealed class AndroidFileDialogService : IFileDialogService
{
    public bool IsAvailable => AndroidFilePickerBridge.PickFiles is not null;

    public async Task<string> SelectSaveFileAsync(
        string? title,
        string fileName,
        string? fileFilter = null,
        string? initialDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return AndroidFilePickerBridge.SaveFile is null
            ? ""
            : await AndroidFilePickerBridge
                .SaveFile(string.IsNullOrWhiteSpace(fileName) ? "save.bin" : fileName)
                .ConfigureAwait(false);
    }

    public async Task<string> SelectFileAsync(
        string? fileFilter = null,
        string? title = null,
        string? initialDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = AndroidFilePickerBridge.PickFiles is null
            ? []
            : await AndroidFilePickerBridge.PickFiles(false).ConfigureAwait(false);
        return files.Length == 0 ? "" : files[0];
    }

    public async Task<string[]> SelectFilesAsync(
        string? fileFilter = null,
        string? title = null,
        string? initialDirectory = null,
        bool allowMultiSelect = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return AndroidFilePickerBridge.PickFiles is null
            ? []
            : await AndroidFilePickerBridge.PickFiles(allowMultiSelect).ConfigureAwait(false);
    }

    public async Task<string> SelectFolderAsync(
        string? title = null,
        string? initialDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return AndroidFilePickerBridge.PickFolder is null
            ? ""
            : await AndroidFilePickerBridge.PickFolder().ConfigureAwait(false);
    }
}
