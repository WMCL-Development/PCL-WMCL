using Android.Content;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace PCL.Android;

[Activity(
    Label = "PCL CE",
    Theme = "@style/Theme.AppCompat.DayNight.NoActionBar",
    Icon = "@android:drawable/ic_menu_manage",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.Landscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AndroidPermissions.Activity = this;
        AndroidFilePicker.Initialize(this);
        AndroidBootstrap.Breadcrumb("activity: onCreate");

        // 必须在 Activity 创建之后再申请：应用初始化阶段还没有 Activity，
        // 之前放在启动步骤里导致通知权限从来没被请求过。
        AndroidPermissions.RequestStartupPermissions();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (AndroidFilePicker.OnActivityResult(requestCode, resultCode, data))
            return;
        AndroidVpnController.OnActivityResult(requestCode, resultCode);
    }

    public override void OnRequestPermissionsResult(
        int requestCode,
        string[] permissions,
        Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        // 通知权限可能刚被授予：让正在运行的 VpnService 重新贴通知。
        if (grantResults.Length > 0 && grantResults.All(r => r == Permission.Granted))
            AndroidVpnController.RefreshNotification();
    }

    protected override void OnPause()
    {
        base.OnPause();

        // 浏览器 OAuth 回调需要启动器在后台也能响应 localhost 请求，
        // 否则系统冻结进程后浏览器会报 ERR_CONNECTION_TIMED_OUT。
        if (PCL.WebServer.IsWebServerRunning("oauth/NatayarkID"))
            OAuthKeepAliveService.Start(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        OAuthKeepAliveService.Stop(this);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        OAuthKeepAliveService.Stop(this);
        AndroidRuntimeSetup.Shutdown();
    }
}
