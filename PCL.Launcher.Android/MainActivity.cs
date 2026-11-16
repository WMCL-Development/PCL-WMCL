using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace PCL.Android;

[Activity(
    Label = "PCL CE",
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    Icon = "@android:drawable/ic_menu_manage",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<AndroidApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .LogToTrace();
}
