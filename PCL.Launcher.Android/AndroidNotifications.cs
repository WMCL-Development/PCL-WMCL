using Android.App;
using Android.Content;
using Android.OS;
using PCL.Core.Logging;

namespace PCL.Android;

/// <summary>
///     通知渠道与补发。MIUI 上首次前台服务通知有时不显示，
///     这里在启动时建好渠道，并在服务启动后补发一条同 id 的通知。
/// </summary>
public static class AndroidNotifications
{
    public const string ChannelId = "pcl_easytier";
    private const int NotificationId = 0xE7;

    public static void EnsureChannel()
    {
        try
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
            var manager = (NotificationManager?)Application.Context
                .GetSystemService(Context.NotificationService);
            if (manager is null) return;
            if (manager.GetNotificationChannel(ChannelId) is not null) return;

            var channel = new NotificationChannel(ChannelId, "PCL CE 联机",
                NotificationImportance.Default)
            {
                Description = "EasyTier 虚拟网络运行状态"
            };
            manager.CreateNotificationChannel(channel);
            LogWrapper.Info("Notification", "已创建通知渠道 " + ChannelId);
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("notification:channel", exception);
        }
    }

    /// <summary>补发一条普通通知，绕开部分机型首次前台服务通知不显示的问题。</summary>
    public static void PostRunningNotification(string text)
    {
        try
        {
            EnsureChannel();
            var context = Application.Context;
            var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            if (manager is null) return;

            var builder = new Notification.Builder(context, ChannelId)
                .SetContentTitle("PCL CE 联机")
                .SetContentText(text)
                .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                .SetOngoing(true);
            manager.Notify(NotificationId, builder.Build());
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("notification:post", exception);
        }
    }

    public static void Cancel(int id = NotificationId)
    {
        try
        {
            var manager = (NotificationManager?)Application.Context
                .GetSystemService(Context.NotificationService);
            manager?.Cancel(id);
        }
        catch
        {
            // 忽略
        }
    }
}
