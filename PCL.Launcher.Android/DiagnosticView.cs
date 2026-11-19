using Android.Content;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Button = Avalonia.Controls.Button;

namespace PCL.Android;

/// <summary>诊断页：显示失败步骤、异常与启动埋点，并可一键复制日志。</summary>
public sealed class DiagnosticView : UserControl
{
    private readonly string _text;

    public DiagnosticView(string message)
    {
        _text = $"FAILED: {message}{Environment.NewLine}{Environment.NewLine}" +
                $"==== LOG ===={Environment.NewLine}{AndroidBootstrap.RecentLog}";

        var copyButton = new Button
        {
            Content = "复制日志",
            Height = 36,
            Width = 96,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        copyButton.Click += (_, _) => CopyToClipboard();

        var body = new SelectableTextBlock
        {
            Text = _text,
            FontFamily = new FontFamily("monospace"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#C62828"))
        };

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                new Border
                {
                    [DockPanel.DockProperty] = Dock.Top,
                    Padding = new Thickness(10, 6),
                    Child = copyButton
                },
                new ScrollViewer { Content = body }
            }
        };
    }

    private void CopyToClipboard()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var manager = (ClipboardManager?)context.GetSystemService(Context.ClipboardService);
            manager?.PrimaryClip = ClipData.NewPlainText("PCL CE log", _text);
        }
        catch (Exception exception)
        {
            AndroidBootstrap.Breadcrumb("COPY_FAIL " + exception.Message);
        }
    }
}
