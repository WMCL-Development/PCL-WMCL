using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;

namespace PCL.Android;

/// <summary>
///     「所有文件访问权限」申请：SAF 只授予 content:// URI 的访问权，
///     启动器按文件路径读写共享存储，需要 MANAGE_EXTERNAL_STORAGE。
/// </summary>
public static class AndroidStoragePermission
{
    public static bool HasAllFilesAccess()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.R) return true;
        return global::Android.OS.Environment.IsExternalStorageManager;
    }

    /// <summary>跳转到系统的「所有文件访问权限」设置页。</summary>
    public static void RequestAllFilesAccess()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            if (Build.VERSION.SdkInt < BuildVersionCodes.R) return;

            var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
            intent.SetData(global::Android.Net.Uri.Parse("package:" + context.PackageName));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
            AndroidBootstrap.Breadcrumb("permission: 已跳转所有文件访问设置");
        }
        catch (Exception exception)
        {
            AndroidBootstrap.RecordFatal("permission:AllFiles", exception);
        }
    }
}
