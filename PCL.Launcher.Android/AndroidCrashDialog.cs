using System;
using System.Text;
using System.Threading.Tasks;
using Android.Content;
using Android.Widget;
using Avalonia.Threading;
using PCL.Core.Logging;
using PCL.Core.UI.Dialogs;

namespace PCL.Android;

/// <summary>Android 崩溃界面：优先用启动器自己的 PCL 弹窗展示，失败时退回系统对话框。</summary>
public static class AndroidCrashDialog
{
    private static bool _showing;

    public static void Show(Exception exception)
    {
        var activity = MainActivity.Current;
        if (activity is null)
        {
            LogWrapper.Error(exception, "Crash", "崩溃时没有可用的 Activity");
            return;
        }

        var content = BuildContent(exception);

        try
        {
            Dispatcher.UIThread.Post(() => ShowFancyDialog(content));
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "Crash", "提交崩溃弹窗失败，改用系统对话框");
            ShowFallbackDialog(activity, content);
        }
    }

    private static void ShowFancyDialog(string content)
    {
        if (_showing)
            return;

        _showing = true;
        try
        {
            var request = new TextDialogRequest
            {
                Title = "PCL CE 崩溃",
                Content = content,
                IsWarn = true,
                Buttons = DialogButtons.From(
                    DialogButton.Create("复制详细信息", () => Copy(content)),
                    DialogButton.Create("退出应用", () => MainActivity.Current?.FinishAffinity()),
                    DialogButton.Create("关闭"))
            };

            _ = DialogService.Instance.ShowAsync(request)
                .ContinueWith(_ => _showing = false, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _showing = false;
            LogWrapper.Error(ex, "Crash", "显示 PCL 崩溃弹窗失败");
            if (MainActivity.Current is { } activity)
                ShowFallbackDialog(activity, content);
        }
    }

    private static string BuildContent(Exception exception)
    {
        var message = new StringBuilder();
        message.AppendLine(exception.Message);
        message.AppendLine();
        message.AppendLine(exception.GetType().FullName);
        message.AppendLine();
        message.Append(exception.StackTrace ?? "(无堆栈)");
        return message.ToString();
    }

    private static void ShowFallbackDialog(MainActivity activity, string content)
    {
        activity.RunOnUiThread(() =>
        {
            try
            {
                var dialog = new AlertDialog.Builder(activity)
                    .SetTitle("PCL CE 崩溃")
                    .SetMessage(content)
                    .SetPositiveButton("复制详细信息", (_, _) => Copy(content))
                    .SetNegativeButton("关闭", (_, _) => { })
                    .SetCancelable(true)
                    .Create();
                dialog.Show();
            }
            catch (Exception ex)
            {
                LogWrapper.Error(ex, "Crash", "显示系统崩溃对话框失败");
            }
        });
    }

    private static void Copy(string content)
    {
        try
        {
            if (MainActivity.Current is not { } activity)
                return;

            if (activity.GetSystemService(Context.ClipboardService) is global::Android.Content.ClipboardManager clipboard)
                clipboard.PrimaryClip = ClipData.NewPlainText("PCL CE 崩溃信息", content);

            Toast.MakeText(activity, "已复制到剪贴板", ToastLength.Short)?.Show();
        }
        catch (Exception ex)
        {
            LogWrapper.Warn("Android", "复制崩溃信息失败：" + ex.Message);
        }
    }
}
