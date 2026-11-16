using System.Net.Http;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PCL.Core.Platform;
using PCL.Platform.Android;

namespace PCL.Android;

/// <summary>P0 诊断视图：展示平台信息并验证网络与下载能力。</summary>
public sealed class MainView : UserControl
{
    private readonly TextBlock _log = new()
    {
        FontFamily = new FontFamily("monospace"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap
    };

    public MainView()
    {
        var info = new StackPanel { Spacing = 4, Margin = new Thickness(16) };

        info.Children.Add(new TextBlock
        {
            Text = "PCL CE · Android P0",
            FontSize = 20,
            FontWeight = FontWeight.Bold
        });

        foreach (var line in DescribeEnvironment())
            info.Children.Add(new TextBlock { Text = line, FontSize = 12, TextWrapping = TextWrapping.Wrap });

        var testButton = new Button { Content = "测试网络（Modrinth）", Margin = new Thickness(0, 12, 0, 0) };
        testButton.Click += async (_, _) => await TestNetworkAsync();

        var openButton = new Button { Content = "打开 PCL 官网", Margin = new Thickness(0, 8, 0, 0) };
        openButton.Click += (_, _) => new AndroidShellLauncher().OpenUrl("https://pclc.cc");

        info.Children.Add(testButton);
        info.Children.Add(openButton);
        info.Children.Add(new ScrollViewer
        {
            Height = 260,
            Margin = new Thickness(0, 12, 0, 0),
            Content = _log
        });

        Content = new ScrollViewer { Content = info };
        _log.Text = "就绪。点击上面的按钮开始测试。";

        Loaded += (_, _) =>
        {
            try
            {
                AndroidFactories.Register();
                Append("平台运行时已初始化");
            }
            catch (Exception ex)
            {
                Append("平台初始化失败：" + ex.Message);
            }
        };
    }

    private static IEnumerable<string> DescribeEnvironment()
    {
        var context = global::Android.App.Application.Context;
        yield return "包名：" + context.PackageName;
        yield return "私有目录：" + context.FilesDir?.AbsolutePath;
        yield return "系统版本：Android " + global::Android.OS.Build.VERSION.Release;
        yield return "ABI：" + string.Join(", ", global::Android.OS.Build.SupportedAbis ?? []);
        yield return "架构：" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        yield return ".NET：" + Environment.Version;
    }

    private async Task TestNetworkAsync()
    {
        Append("请求 Modrinth 搜索……");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", "PCLCE-Android/0.1.0");
            var json = await client.GetStringAsync(
                "https://api.modrinth.com/v2/search?limit=3&query=sodium");
            using var document = JsonDocument.Parse(json);
            var hits = document.RootElement.GetProperty("hits");
            Append($"成功，返回 {hits.GetArrayLength()} 条：");
            foreach (var hit in hits.EnumerateArray())
                Append("· " + hit.GetProperty("title").GetString());
        }
        catch (Exception ex)
        {
            Append("失败：" + ex.GetType().Name + " " + ex.Message);
        }
    }

    private void Append(string text)
    {
        Dispatcher.UIThread.Post(() =>
            _log.Text = (_log.Text == "就绪。点击上面的按钮开始测试。" ? "" : _log.Text + "\n") + text);
    }
}
