using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using PCL.Core.Logging;

namespace PCL.Android;

/// <summary>VPN 授权与 tun 生命周期管理。</summary>
public static class AndroidVpnController
{
    private const int PrepareRequestCode = 0x5D2;
    private static TaskCompletionSource<bool>? _pending;

    /// <summary>确保已获得 VPN 授权；未授权时会弹出系统对话框。</summary>
    public static Task<bool> EnsurePreparedAsync(Activity? activity)
    {
        if (activity is null) return Task.FromResult(false);

        var intent = VpnService.Prepare(activity);
        if (intent is null)
        {
            LogWrapper.Info("EasyTier", "VPN 已授权");
            return Task.FromResult(true);
        }

        _pending = new TaskCompletionSource<bool>();
        try
        {
            activity.StartActivityForResult(intent, PrepareRequestCode);
            AndroidBootstrap.Breadcrumb("vpn: 请求授权");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("vpn:prepare", exception);
            _pending.TrySetResult(false);
        }

        return _pending.Task;
    }

    /// <summary>由 MainActivity 在 OnActivityResult 里回调。</summary>
    public static void OnActivityResult(int requestCode, Result resultCode)
    {
        if (requestCode != PrepareRequestCode) return;
        var granted = resultCode == Result.Ok;
        LogWrapper.Info("EasyTier", "VPN 授权结果：" + (granted ? "已允许" : "被拒绝"));
        _pending?.TrySetResult(granted);
        _pending = null;
    }

    /// <summary>启动 VpnService 建立 tun。</summary>
    public static void Start()
    {
        try
        {
            var context = Application.Context;
            var intent = new Intent(context, typeof(EasyTierVpnService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
            AndroidBootstrap.Breadcrumb("vpn: 已启动服务");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("vpn:start", exception);
        }
    }

    /// <summary>重新贴一次前台通知（通知权限刚授予时用）。</summary>
    public static void RefreshNotification()
    {
        try
        {
            var context = Application.Context;
            var intent = new Intent(context, typeof(EasyTierVpnService));
            intent.SetAction(EasyTierVpnService.ActionRefresh);
            context.StartService(intent);
            AndroidBootstrap.Breadcrumb("vpn: 刷新通知");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("vpn:refresh", exception);
        }
    }

    /// <summary>停止 VpnService。</summary>
    public static void Stop()
    {
        try
        {
            var context = Application.Context;
            // 先让服务自己关 tun 并 StopSelf，再兜底 StopService。
            var stopIntent = new Intent(context, typeof(EasyTierVpnService));
            stopIntent.SetAction(EasyTierVpnService.ActionStop);
            context.StartService(stopIntent);
            context.StopService(new Intent(context, typeof(EasyTierVpnService)));
            AndroidBootstrap.Breadcrumb("vpn: 已请求停止");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("vpn:stop", exception);
        }
    }
}
