using Android.App;
using Android.Content.PM;
using Android.Net;
using Android.Content;
using Android.OS;
using PCL.Core.Logging;

namespace PCL.Android;

/// <summary>
///     EasyTier 的 tun 载体：Android 不允许应用自己开 /dev/net/tun，
///     必须由 VpnService 建立，再把 ParcelFileDescriptor 交给 EasyTier 核心。
///     当前阶段只验证「授权 + 建立 tun + 拿到 fd」，尚未把 fd 传给核心
///     （公开的 easytier-core CLI 没有接收 fd 的参数，需要自己编译带 tun-fd 的库）。
/// </summary>
[Service(
    Permission = "android.permission.BIND_VPN_SERVICE",
    Exported = false,
    ForegroundServiceType = (ForegroundService)1)]
[IntentFilter(["android.net.VpnService"])]
public sealed class EasyTierVpnService : VpnService
{
    public const string ActionStop = "pcl.easytier.stop";
    public const string ActionRefresh = "pcl.easytier.refresh";

    private const string ChannelId = "pcl_easytier";
    private const int NotificationId = 0xE7;

    private ParcelFileDescriptor? _descriptor;

    /// <summary>本次要使用的虚拟 IPv4，启动前由 EasyTier 流程设置。</summary>
    public static string RequestedIpv4 = "10.126.126.2";

    /// <summary>tun 的文件描述符；-1 表示未建立。</summary>
    public static int Fd { get; private set; } = -1;

    public static bool IsRunning => Fd >= 0;

    public override void OnCreate()
    {
        base.OnCreate();
        AndroidBootstrap.Breadcrumb("vpn: onCreate");
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        AndroidBootstrap.Breadcrumb("vpn: onStartCommand");

        if (intent?.Action == ActionRefresh)
        {
            // 通知权限后授予时，重新贴一次前台通知，否则第一次启动看不到。
            if (Fd >= 0)
                StartForeground(NotificationId, BuildNotification(), (ForegroundService)1);
            return StartCommandResult.NotSticky;
        }

        if (intent?.Action == ActionStop)
        {
            Shutdown();
            return StartCommandResult.NotSticky;
        }
        StartForeground(NotificationId, BuildNotification(), (ForegroundService)1);

        try
        {
            var builder = new Builder(this)
                .SetSession("PCL CE EasyTier")
                .SetMtu(1380)
                .AddAddress(RequestedIpv4, 24)
                .AddRoute("10.126.126.0", 24)
                .AddDnsServer("223.5.5.5");
            builder.SetConfigureIntent(null);

            _descriptor = builder.Establish();
            if (_descriptor is null)
            {
                LogWrapper.Error("EasyTier", "VpnService 建立 tun 失败：Establish 返回 null");
                return StartCommandResult.RedeliverIntent;
            }

            Fd = _descriptor.Fd;
            AndroidNotifications.PostRunningNotification("EasyTier 虚拟网络运行中");
            LogWrapper.Info("EasyTier", $"VpnService tun 已建立，fd={Fd}");
            AndroidBootstrap.Breadcrumb($"vpn: fd={Fd}");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("vpn:establish", exception);
        }

        return StartCommandResult.RedeliverIntent;
    }

    /// <summary>关闭 tun 并停掉自己：VPN 图标与前台通知都会随之消失。</summary>
    private void Shutdown()
    {
        try
        {
            _descriptor?.Close();
            _descriptor = null;
            Fd = -1;
            StopForeground(StopForegroundFlags.Remove);
            AndroidNotifications.Cancel();
            AndroidBootstrap.Breadcrumb("vpn: shutdown");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("vpn:shutdown", exception);
        }

        StopSelf();
    }

    public override void OnDestroy()
    {
        try
        {
            _descriptor?.Close();
            _descriptor = null;
            Fd = -1;
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("vpn:destroy", exception);
        }

        StopForeground(StopForegroundFlags.Remove);
        AndroidBootstrap.Breadcrumb("vpn: onDestroy");
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private Notification BuildNotification()
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId, "PCL CE 联机", NotificationImportance.Low);
            manager?.CreateNotificationChannel(channel);
        }

        return new Notification.Builder(this, ChannelId)
            .SetContentTitle("PCL CE 联机")
            .SetContentText("EasyTier 虚拟网络运行中")
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .Build();
    }
}
