using Android.Content;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Android.OS;
using Avalonia.Android;

namespace PCL.Android;

[Activity(
    Label = "PCL CE",
    Theme = "@style/AppTheme",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.Landscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private static MainActivity? _current;
    private View? _splashOverlay;

    /// <summary>当前 Activity，供崩溃对话框等原生界面使用。</summary>
    public static MainActivity? Current => _current;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _current = this;
        ShowSplashOverlay();
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

    /// <summary>返回键：有页面历史时返回上一页，否则交给系统退出。</summary>
    public override void OnBackPressed()
    {
        try
        {
            if (PCL.Navigation.PageRouter.CanGoBack)
            {
                PCL.Navigation.PageRouter.Back();
                return;
            }
        }
        catch (Exception exception)
        {
            PCL.Core.Logging.LogWrapper.Warn("Android", "返回键处理失败：" + exception.Message);
        }

        base.OnBackPressed();
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

    /// <summary>启动遮罩：Avalonia 首帧渲染前先显示品牌背景 + 图标 + 加载动画，避免冷启动白屏。</summary>
    private void ShowSplashOverlay()
    {
        try
        {
            if (Window?.DecorView is not ViewGroup root)
                return;

            var density = Resources?.DisplayMetrics?.Density ?? 1f;

            var overlay = new FrameLayout(this)
            {
                Clickable = false,
                Focusable = false
            };
            overlay.SetBackgroundColor(Color.ParseColor("#FF1C8CE8"));

            var content = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical
            };
            content.SetGravity(GravityFlags.Center);

            var logoSize = (int)(96 * density);
            var logo = new ImageView(this);
            logo.SetImageResource(Resource.Drawable.splash_logo);
            logo.SetScaleType(ImageView.ScaleType.FitCenter);
            content.AddView(logo, new LinearLayout.LayoutParams(logoSize, logoSize));

            var progressSize = (int)(28 * density);
            var progress = new ProgressBar(this)
            {
                Indeterminate = true,
                IndeterminateTintList = global::Android.Content.Res.ColorStateList.ValueOf(Color.White)
            };
            var progressParams = new LinearLayout.LayoutParams(progressSize, progressSize)
            {
                TopMargin = (int)(18 * density)
            };
            content.AddView(progress, progressParams);

            overlay.AddView(content, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

            root.AddView(overlay, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
            _splashOverlay = overlay;
        }
        catch (Exception ex)
        {
            PCL.Core.Logging.LogWrapper.Warn("Android", "显示启动遮罩失败：" + ex.Message);
        }
    }

    /// <summary>UI 首帧可见后由 DesktopShell 调用，移除原生启动遮罩。</summary>
    public static void HideSplash()
    {
        var activity = _current;
        if (activity is null)
            return;

        activity.RunOnUiThread(() =>
        {
            try
            {
                if (activity._splashOverlay?.Parent is ViewGroup parent)
                    parent.RemoveView(activity._splashOverlay);
                activity._splashOverlay = null;
            }
            catch (Exception ex)
            {
                PCL.Core.Logging.LogWrapper.Warn("Android", "移除启动遮罩失败：" + ex.Message);
            }
        });
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        OAuthKeepAliveService.Stop(this);
        AndroidRuntimeSetup.Shutdown();
        _current = null;
    }
}
