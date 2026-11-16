using System;
using System.Collections.Generic;
using PCL;

namespace PCL.Cli;

/// <summary>
///     终端版启动界面：把启动管线的界面回调换成命令行输出。
///     与界面层的 LaunchUiBridge 职责相同，互不依赖。
/// </summary>
internal sealed class CliLaunchUi : ILaunchUi
{
    public int ShowMessageBox(string text, string? title = null, string? button1 = null,
        string? button2 = "", string? button3 = "", bool isWarn = false, bool forceWait = false,
        Action? button1Action = null)
    {
        CliConsole.Error((title is null ? "" : title + "：") + text);
        button1Action?.Invoke();
        return 1;
    }

    public void SetGpuPreference(string javaExePath, bool preference)
    {
    }

    public int? ShowSelect(string caption, IReadOnlyList<string> options)
    {
        CliConsole.Line(caption);
        for (var index = 0; index < options.Count; index++)
            CliConsole.Line("  [" + (index + 1) + "] " + options[index]);
        return options.Count > 0 ? 1 : null;
    }

    public void OpenExplorer(string path) => CliConsole.Line("目录：" + path);

    public void PrepareLaunchNavigation()
    {
    }

    public void RefreshLaunchButtons()
    {
    }

    public void RefreshLaunchPage(bool show)
    {
    }

    public void AppendLaunchLog(string text) => CliConsole.Line(text.TrimEnd());

    public void ClearLaunchLog()
    {
    }

    public void ShowLoginPage()
    {
    }

    public void ShowLaunchingPage()
    {
    }

    public void RefreshLaunching()
    {
    }

    public void AttachLogPage(GameWatcher.Watcher watcher)
    {
    }

    public void SetMainWindowState(LaunchWindowState state)
    {
    }

    public (double Width, double Height) GetMainPanSize() => (854, 480);
}
