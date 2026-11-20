using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.UI;
using PCL.Navigation;
using PCL.UI.Controls;
using Button = Avalonia.Controls.Button;
using ProgressBar = Avalonia.Controls.ProgressBar;

namespace PCL.Android;

internal sealed class AndroidJavaSetupPageFactory : IJavaSetupPageFactory
{
    public Control Create() => new AndroidJavaSetupPage();
}

internal sealed class AndroidJavaSetupPage : MyPageRight, IRefreshable
{
    private const string TextPrefix = "Setup.Java.Android.";
    private static readonly global::Android.Content.Context Context = global::Android.App.Application.Context;
    private readonly ComboBox _versions = new() { ItemsSource = JavaRuntimePackage.Catalog, SelectedIndex = 3 };
    private readonly Button _run = new();
    private readonly Button _cancel = new();
    private readonly Button _graphics = new();
    private readonly TextBlock _state = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { IsVisible = false };
    private CancellationTokenSource? _operation;

    public AndroidJavaSetupPage()
    {
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap };
        description.SetLocalizedValue(TextBlock.TextProperty, TextPrefix + "Description");
        _run.SetLocalizedValue(ContentControl.ContentProperty, TextPrefix + "Run");
        _cancel.SetLocalizedValue(ContentControl.ContentProperty, "Common.Action.Cancel");
        _graphics.SetLocalizedValue(ContentControl.ContentProperty, TextPrefix + "GraphicsProbe");
        _run.Click += async (_, _) => await RunProbeAsync();
        _cancel.Click += (_, _) => _operation?.Cancel();
        _graphics.Click += (_, _) => OpenGraphicsProbe();
        _versions.SelectionChanged += (_, _) => Refresh();
        var actions = new StackPanel
        {
            Spacing = 8,
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Children = { _run, _cancel, _graphics }
        };
        var content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children = { description, _versions, _state, _progress, actions }
        };
        if (AndroidJavaPaths.IsSupported && AndroidLwjglProbe.IsAvailable)
        {
            var lwjgl = new Button { Content = "LWJGL 3.3.3 / Java 21" };
            lwjgl.Click += (_, _) =>
            {
                if (_operation is null)
                    AndroidLwjglProbeActivity.Open(MainActivity.Current is { } activity ? activity : Context);
            };
            content.Children.Add(lwjgl);
            var vanilla = new Button { Content = "原版 1.21.1（探针账号）" };
            vanilla.Click += (_, _) =>
            {
                if (_operation is null)
                    AndroidLwjglProbeActivity.Open(MainActivity.Current is { } activity ? activity : Context, vanilla: true);
            };
            content.Children.Add(vanilla);
        }
        var card = new MyCard { Children = { content } };
        PanScroll = new MyScrollViewer { Content = new Border { Padding = new Thickness(20), Child = card } };
        Child = PanScroll;
        AttachedToVisualTree += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        if (_operation is not null)
            return;

        _versions.IsEnabled = AndroidJavaPaths.IsSupported;
        _graphics.IsVisible = AndroidJavaPaths.IsSupported;
        _run.IsEnabled = AndroidJavaPaths.IsSupported && _versions.SelectedItem is JavaRuntimePackage;
        _cancel.IsVisible = false;
        if (!AndroidJavaPaths.IsSupported)
        {
            _state.Text = Text("Unsupported");
            return;
        }

        if (_versions.SelectedItem is not JavaRuntimePackage package)
            return;

        var installed = JavaRuntimeInstaller.FindInstalled(AndroidJavaPaths.Runtimes(Context), package);
        _state.Text = installed is not null ? Text("Installed", package.Version) :
            Text("NotInstalled", package.ArchiveSize / 1048576d);
    }

    private async Task RunProbeAsync()
    {
        if (_operation is not null || !AndroidJavaPaths.IsSupported || _versions.SelectedItem is not JavaRuntimePackage package)
            return;

        using var operation = new CancellationTokenSource();
        _operation = operation;
        _run.IsEnabled = false;
        _graphics.IsEnabled = false;
        _versions.IsEnabled = false;
        _cancel.IsVisible = true;
        _progress.IsVisible = true;
        ShowIndeterminate("Preparing");
        var installProgress = new Progress<JavaInstallProgress>(progress =>
        {
            if (_operation != operation)
                return;
            switch (progress.Stage)
            {
                case JavaInstallStage.Downloading:
                    _state.Text = Text("Downloading");
                    _progress.IsIndeterminate = false;
                    _progress.Maximum = package.ArchiveSize;
                    _progress.Value = progress.Bytes;
                    break;
                case JavaInstallStage.Verifying:
                    ShowIndeterminate("Verifying");
                    break;
                case JavaInstallStage.Extracting:
                    ShowIndeterminate("Extracting");
                    break;
            }
        });

        try
        {
            var plan = await PrepareProbeAsync(operation.Token);
            await AndroidJvmSession.RunAsync(Context, package, plan, installProgress,
                () => Dispatcher.UIThread.Post(() =>
                {
                    if (_operation == operation) ShowIndeterminate("Starting");
                }), TimeSpan.FromSeconds(60), operation.Token);

            var output = File.ReadLines(AndroidJavaPaths.Log(Context))
                .FirstOrDefault(line => line.StartsWith("WMCL_JVM_OK ", StringComparison.Ordinal));
            if (output is null)
                throw new InvalidOperationException(Text("MissingOutput"));
            LogWrapper.Info("Android JVM", output);
            _state.Text = Text("Passed", output);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            _state.Text = Text("Cancelled");
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "Android JVM", "JVM 自检失败");
            _state.Text = Text("Failed", exception.Message);
        }
        finally
        {
            _operation = null;
            _run.IsEnabled = true;
            _graphics.IsEnabled = true;
            _versions.IsEnabled = true;
            _cancel.IsVisible = false;
            _progress.IsVisible = false;
        }
    }

    private void OpenGraphicsProbe()
    {
        if (_operation is not null || !AndroidJavaPaths.IsSupported)
            return;

        try
        {
            AndroidGraphicsProbeActivity.Open(MainActivity.Current is { } activity ? activity : Context, Text("GraphicsProbe"));
        }
        catch (Exception exception)
        {
            LogWrapper.Warn(exception, "Android JVM", "无法打开图形自检");
            _state.Text = Text("Failed", exception.Message);
        }
    }

    private static async Task<JavaLaunchPlan> PrepareProbeAsync(CancellationToken token)
    {
        var directory = Path.Combine(Context.CacheDir!.AbsolutePath, "jvm-probe");
        Directory.CreateDirectory(directory);
        const string resource = "WmclJvmProbe.class";
        await using var input = typeof(AndroidJavaSetupPage).Assembly.GetManifestResourceStream(resource)
                                ?? throw new InvalidOperationException("缺少 JVM 自检探针资源。");
        await using (var output = File.Create(Path.Combine(directory, resource)))
            await input.CopyToAsync(output, token);
        return new JavaLaunchPlan(
            ["-Xms16m", "-Xmx128m", "-Djava.awt.headless=true", "-Dwmcl.probe=two words"], "WmclJvmProbe",
            ["space and \"quotes\" \\ tail", "", "中文🍃"], directory, classPathEntries: [directory]);
    }

    private void ShowIndeterminate(string key)
    {
        _state.Text = Text(key);
        _progress.IsIndeterminate = true;
    }

    private static string Text(string key, params object?[] arguments) => Lang.Text(TextPrefix + key, arguments);
}
