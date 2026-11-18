using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace PCL.Android;

/// <summary>启动时申请通知与媒体读取权限（Android 13+ 拆分为细分权限）。</summary>
public static class AndroidPermissions
{
    private const int RequestCode = 0x5C1;

    public static void RequestStartupPermissions()
    {
        try
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.M) return;
            if (Activity is not { } activity) return;

            var wanted = new List<string> { Manifest.Permission.PostNotifications };

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                wanted.Add(Manifest.Permission.ReadMediaImages);
                wanted.Add(Manifest.Permission.ReadMediaVideo);
            }
            else
            {
                wanted.Add(Manifest.Permission.ReadExternalStorage);
            }

            var missing = wanted
                .Where(permission =>
                    ContextCompat.CheckSelfPermission(activity, permission) != Permission.Granted)
                .ToArray();
            if (missing.Length == 0) return;

            ActivityCompat.RequestPermissions(activity, missing, RequestCode);
            AndroidBootstrap.Breadcrumb($"permission: 申请 {missing.Length} 项");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("permission:Startup", exception);
        }
    }

    /// <summary>由 MainActivity 在 OnCreate 时登记，供权限申请使用。</summary>
    public static Activity? Activity { get; set; }
}
