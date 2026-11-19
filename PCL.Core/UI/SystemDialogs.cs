using System;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Platform;
using PCL.Core.UI.Threading;

namespace PCL.Core.UI;

/// <summary>提供文件和文件夹对话框相关的实用方法。具体实现由组合根注册到 <see cref="ApplicationRuntime" />。</summary>
public static class SystemDialogs
{
    private static IFileDialogService? Service =>
        ApplicationRuntime.IsInitialized ? ApplicationRuntime.Current.Ui.FileDialogs : null;

    /// <summary>当前平台是否有可用的文件对话框实现。</summary>
    public static bool IsAvailable => Service?.IsAvailable ?? false;

    /// <summary>异步选择文件夹（Android 等单视图平台必须用这个，不能阻塞 UI 线程）。</summary>
    public static Task<string> SelectFolderAsync(string? title = null, string? initialDirectory = null)
    {
        return Service?.SelectFolderAsync(title, initialDirectory) ?? Task.FromResult("");
    }

    /// <summary>异步选择文件。</summary>
    public static Task<string> SelectFileAsync(
        string? fileFilter = null,
        string? title = null,
        string? initialDirectory = null)
    {
        return Service?.SelectFileAsync(fileFilter, title, initialDirectory) ?? Task.FromResult("");
    }

    // ---- 后台线程用的同步包装 ----
    // Android 不支持在 UI 线程上嵌套消息循环，所以这些只能在非 UI 线程调用；
    // 桌面端行为与原来的同步 API 完全一致。

    /// <summary>在后台线程上同步选择文件夹。</summary>
    public static string SelectFolderBlocking(string? title = null, string? initialDirectory = null)
    {
        return WaitFor(SelectFolderAsync(title, initialDirectory)) ?? "";
    }

    /// <summary>在后台线程上同步选择文件。</summary>
    public static string SelectFileBlocking(
        string? fileFilter = null,
        string? title = null,
        string? initialDirectory = null)
    {
        return WaitFor(SelectFileAsync(fileFilter, title, initialDirectory)) ?? "";
    }

    /// <summary>在后台线程上同步多选文件。</summary>
    public static string[] SelectFilesBlocking(
        string? fileFilter = null,
        string? title = null,
        string? initialDirectory = null,
        bool allowMultiSelect = true)
    {
        return WaitFor(SelectFilesAsync(fileFilter, title, initialDirectory, allowMultiSelect))
               ?? [];
    }

    /// <summary>在后台线程上同步选择保存位置。</summary>
    public static string SelectSaveFileBlocking(
        string? title,
        string fileName,
        string? fileFilter = null,
        string? initialDirectory = null)
    {
        return WaitFor(
                   SelectSaveFileAsync(title, fileName, fileFilter, initialDirectory))
               ?? "";
    }

    /// <summary>异步多选文件。</summary>
    public static Task<string[]> SelectFilesAsync(
        string? fileFilter = null,
        string? title = null,
        string? initialDirectory = null,
        bool allowMultiSelect = true)
    {
        return Service?.SelectFilesAsync(fileFilter, title, initialDirectory, allowMultiSelect)
               ?? Task.FromResult<string[]>([]);
    }

    /// <summary>异步保存文件。</summary>
    public static Task<string> SelectSaveFileAsync(
        string? title,
        string fileName,
        string? fileFilter = null,
        string? initialDirectory = null)
    {
        return Service?.SelectSaveFileAsync(title, fileName, fileFilter, initialDirectory)
               ?? Task.FromResult("");
    }

    /// <summary>显示保存文件对话框，要求用户选择保存位置。</summary>
    /// <returns>用户选择的完整文件路径，如果取消或无可用的 UI 实现则返回空字符串。</returns>
    public static Task<string> SelectSaveFileAsync(
        string? title,
        string fileName,
        string? fileFilter = null,
        string? initialDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return Service?.SelectSaveFileAsync(
                title,
                fileName,
                fileFilter,
                initialDirectory,
                cancellationToken)
            ?? Task.FromResult("");
    }

    /// <summary>显示打开文件对话框，要求用户选择单个文件。</summary>
    /// <returns>用户选择的完整文件路径，如果取消则返回空字符串。</returns>
    public static Task<string> SelectFileAsync(
        string? fileFilter = null,
        string? title = null,
        string? initialDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return Service?.SelectFileAsync(
                fileFilter,
                title,
                initialDirectory,
                cancellationToken)
            ?? Task.FromResult("");
    }

    /// <summary>显示打开文件对话框，要求用户选择文件。</summary>
    /// <returns>用户选择的文件路径数组，如果取消则返回空数组。</returns>
    public static Task<string[]> SelectFilesAsync(
        string? fileFilter = null,
        string? title = null,
        string? initialDirectory = null,
        bool allowMultiSelect = true,
        CancellationToken cancellationToken = default)
    {
        return Service?.SelectFilesAsync(
                fileFilter,
                title,
                initialDirectory,
                allowMultiSelect,
                cancellationToken)
            ?? Task.FromResult(Array.Empty<string>());
    }

    /// <summary>显示文件夹选择对话框，要求用户选择一个文件夹。</summary>
    /// <returns>用户选择的文件夹路径（以 \ 结尾），如果取消则返回空字符串。</returns>
    public static Task<string> SelectFolderAsync(
        string? title = null,
        string? initialDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return Service?.SelectFolderAsync(
                title,
                initialDirectory,
                cancellationToken)
            ?? Task.FromResult("");
    }

    /// <summary>仅供工作线程使用的兼容入口。界面线程不得调用，否则会抛出异常；新代码应使用 <c>Select*Async</c>。</summary>
    public static string SelectSaveFileBlockingOnWorkerThread(
        string? title,
        string fileName,
        string? fileFilter = null,
        string? initialDirectory = null)
    {
        return WaitOnWorkerThread(() => SelectSaveFileAsync(
            title,
            fileName,
            fileFilter,
            initialDirectory));
    }

    /// <summary>仅供工作线程使用的兼容入口。</summary>
    public static string SelectFileBlockingOnWorkerThread(
        string? fileFilter = null,
        string? title = null,
        string? initialDirectory = null)
    {
        return WaitOnWorkerThread(() => SelectFileAsync(fileFilter, title, initialDirectory));
    }

    /// <summary>仅供工作线程使用的兼容入口。</summary>
    public static string[] SelectFilesBlockingOnWorkerThread(
        string? fileFilter = null,
        string? title = null,
        string? initialDirectory = null,
        bool allowMultiSelect = true)
    {
        return WaitOnWorkerThread(() => SelectFilesAsync(
            fileFilter,
            title,
            initialDirectory,
            allowMultiSelect));
    }

    /// <summary>仅供工作线程使用的兼容入口。</summary>
    public static string SelectFolderBlockingOnWorkerThread(
        string? title = null,
        string? initialDirectory = null)
    {
        return WaitOnWorkerThread(() => SelectFolderAsync(title, initialDirectory));
    }

    private static T WaitOnWorkerThread<T>(Func<Task<T>> action)
    {
        return UiDispatcher.CheckAccess()
            ? throw new InvalidOperationException("不能在界面线程同步等待文件对话框，请改用异步方法。")
            : action().GetAwaiter().GetResult();
    }

    /// <summary>后台线程上等待异步对话框；UI 线程上直接抛异常（Android 不能嵌套消息循环）。</summary>
    private static T WaitFor<T>(Task<T> task)
    {
        if (!task.IsCompleted
            && ApplicationRuntime.IsInitialized
            && ApplicationRuntime.Current.Ui.Dispatcher.CheckAccess())
            throw new NotSupportedException(
                "文件对话框不能在 UI 线程同步等待，请改用异步 API。");
        return task.GetAwaiter().GetResult();
    }
}
