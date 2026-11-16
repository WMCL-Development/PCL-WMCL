using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using PCL.Platform.Android;

namespace PCL.Android;

/// <summary>Android 应用入口：单视图生命周期，P0 阶段只验证平台层与网络。</summary>
public class AndroidApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = new MainView();

        base.OnFrameworkInitializationCompleted();
    }
}
