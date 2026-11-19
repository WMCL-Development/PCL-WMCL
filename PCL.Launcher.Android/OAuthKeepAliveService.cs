using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace PCL.Android;

/// <summary>
///     等待浏览器 OAuth 回调期间的前台服务。
///     Android 会把切到后台的启动器冻结，导致浏览器无法连接到 localhost 回调端口
///     （表现为 ERR_CONNECTION_TIMED_OUT），这里用前台服务把进程保活。
/// </summary>
[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeDataSync)]
public class OAuthKeepAliveService : Service
{
    private const string ChannelId = "pcl-oauth-wait";
    private const int NotificationId = 0x0A11;
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(3);

    public static void Start(Context context)
    {
        try
        {
            var intent = new Intent(context, typeof(OAuthKeepAliveService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Exception)
        {
            // 保活失败不应该影响登录流程本身
        }
    }

    public static void Stop(Context context)
    {
        try
        {
            context.StopService(new Intent(context, typeof(OAuthKeepAliveService)));
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateChannel();

        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);
        var icon = 0;
        try
        {
            icon = PackageManager?.GetApplicationInfo(PackageName ?? string.Empty, 0)?.Icon ?? 0;
        }
        catch (Exception)
        {
            // 取应用图标失败时退回系统图标
        }

        if (icon == 0) icon = Resource.Mipmap.ic_launcher;

        var notification = builder
            .SetContentTitle("正在等待浏览器登录")
            .SetContentText("完成浏览器里的授权后会自动返回启动器")
            .SetSmallIcon(icon)
            .SetOngoing(true)
            .Build();

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        else
            StartForeground(NotificationId, notification);

        // 超时自动结束，避免通知常驻
        new Handler(Looper.MainLooper!).PostDelayed(StopSelf, (long)MaxLifetime.TotalMilliseconds);
        return StartCommandResult.NotSticky;
    }

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager is null || manager.GetNotificationChannel(ChannelId) is not null) return;

        var channel = new NotificationChannel(
            ChannelId,
            "浏览器登录",
            NotificationImportance.Min);
        manager.CreateNotificationChannel(channel);
    }

    public override IBinder? OnBind(Intent? intent) => null;
}
