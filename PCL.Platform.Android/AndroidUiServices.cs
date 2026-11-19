using PCL.Core.Platform;

namespace PCL.Platform.Android;

/// <summary>Android 窗口互操作：无法枚举与操纵其他应用窗口。</summary>
public sealed class AndroidWindowInterop : IWindowInterop
{
    public (nint Handle, string Title)? TryFindGameWindow(int processId) => null;
    public void SetWindowTitle(nint handle, string title) { }
    public void MaximizeWindow(nint handle) { }
}

/// <summary>Android 系统主题：跟随系统深色模式开关。</summary>
public sealed class AndroidSystemThemeProvider : ISystemThemeProvider
{
    public bool IsAvailable => true;

    public bool IsDarkMode =>
        (global::Android.App.Application.Context.Resources?.Configuration?.UiMode
            & global::Android.Content.Res.UiMode.NightMask) == global::Android.Content.Res.UiMode.NightYes;

    public event EventHandler? ThemeChanged
    {
        add { }
        remove { }
    }
}

/// <summary>Android 通知：投递到通知栏，需要 POST_NOTIFICATIONS 权限（Android 13+）。</summary>
public sealed class AndroidNotificationService : ISystemNotificationService
{
    private const string ChannelId = "pcl_ce_default";
    private static bool _channelCreated;

    public bool IsAvailable => true;

    public void Show(string title, string message)
    {
        var context = global::Android.App.Application.Context;
        var manager = (global::Android.App.NotificationManager?)context.GetSystemService(
            global::Android.Content.Context.NotificationService);
        if (manager is null) return;

        if (!_channelCreated)
        {
            var channel = new global::Android.App.NotificationChannel(
                ChannelId, "PCL CE", global::Android.App.NotificationImportance.Default);
            manager.CreateNotificationChannel(channel);
            _channelCreated = true;
        }

        var builder = new global::Android.App.Notification.Builder(context, ChannelId)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo);
        manager.Notify(Environment.TickCount, builder.Build());
    }
}

/// <summary>Android Shell：用系统 Intent 打开链接或文件。</summary>
public sealed class AndroidShellLauncher : IShellLauncher
{
    public void OpenPath(string path, string? workingDirectory = null, string? arguments = null)
    {
        // 页面里常用 OpenPath 打开官网链接，这里按 URL 处理。
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            OpenUrl(path);
            return;
        }

        var fullPath = path;
        try
        {
            fullPath = System.IO.Path.GetFullPath(path);
        }
        catch (Exception)
        {
            // 保留原始路径
        }

        var isPublic = fullPath.StartsWith(PrimaryStorageRoot, StringComparison.OrdinalIgnoreCase);
        if (!isPublic || (!System.IO.Directory.Exists(fullPath) && !System.IO.File.Exists(fullPath)))
        {
            ShowCannotOpenDialog(path, isPublic);
            return;
        }

        // 公共存储允许通过 file:// 把目录交给文件管理器；需要关掉 StrictMode 的
        // FileUriExposedException 检查（否则 StartActivity 直接抛异常）。
        TryDisableFileUriExposure();
        try
        {
            var context = global::Android.App.Application.Context;
            var file = new global::Java.IO.File(fullPath);
            var uri = global::Android.Net.Uri.FromFile(file);

            var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionView);
            var isDirectory = System.IO.Directory.Exists(fullPath);
            intent.SetDataAndType(uri, isDirectory ? "resource/folder" : "application/octet-stream");
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            intent.AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission);
            context.StartActivity(intent);
        }
        catch (Exception exception)
        {
            PCL.Core.Logging.LogWrapper.Warn("Shell", "打开路径失败：" + exception.Message);
            ShowCannotOpenDialog(path, isPublic);
        }
    }

    private const string PrimaryStorageRoot = "/storage/emulated/0";

    /// <summary>公共目录用 file:// 交给文件管理器时需要绕过 StrictMode 的 FileUriExposedException。</summary>
    private static void TryDisableFileUriExposure()
    {
        try
        {
            typeof(global::Android.OS.StrictMode)
                .GetMethod(
                    "disableDeathOnFileUriExposure",
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic)?
                .Invoke(null, null);
        }
        catch (Exception)
        {
            // 某些 ROM 上该方法不存在，忽略即可，后续会走兜底弹窗。
        }
    }

    /// <summary>无法打开目录时：公共目录提示没有可用的文件管理器，私有目录提示系统限制。</summary>
    private static void ShowCannotOpenDialog(string path, bool isPublic)
    {
        try
        {
            PCL.Core.Platform.ApplicationRuntime
                .GetService<PCL.Core.Platform.IClipboardService>()?
                .SetTextAsync(path);

            var content = isPublic
                ? "没有找到可以打开该文件夹的应用。路径已复制到剪贴板，可以粘贴到文件管理器中使用。"
                : "该目录位于启动器的应用私有空间，Android 系统文件管理器无权访问。路径已复制到剪贴板，可以粘贴到支持访问应用目录的文件管理器（如 MT 管理器、Shizuku 授权工具）中使用。";

            PCL.Core.UI.Dialogs.DialogService.Instance.ShowNotification(
                new PCL.Core.UI.Dialogs.TextDialogRequest
                {
                    Title = "无法直接打开目录",
                    Content = string.Join(
                        System.Environment.NewLine,
                        content,
                        string.Empty,
                        path),
                    IsWarn = true,
                    Buttons = PCL.Core.UI.Dialogs.DialogButtons.Ok
                });
        }
        catch (Exception exception)
        {
            PCL.Core.Logging.LogWrapper.Warn("Shell", "复制路径失败：" + exception.Message);
        }
    }

    public void OpenUrl(string url)
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var uri = global::Android.Net.Uri.Parse(url)!;
            var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionView, uri);
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception exception)
        {
            PCL.Core.Logging.LogWrapper.Warn("Shell", "打开链接失败：" + exception.Message);
        }
    }
}

