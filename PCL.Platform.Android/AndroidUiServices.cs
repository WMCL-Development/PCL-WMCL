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
