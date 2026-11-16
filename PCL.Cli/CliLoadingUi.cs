using System;
using PCL;

namespace PCL.Cli;

/// <summary>
///     ILoadingUi 的终端实现：把加载器的进度画成一行文本。
///     换成 TUI 时替换这个类即可，业务层不需要改动。
/// </summary>
internal sealed class CliLoadingUi : ILoadingUi
{
    private string? _current;

    public void TaskAdded(Loading.LoaderBase loader) => Track(loader);

    public void TaskRemoved(Loading.LoaderBase loader)
    {
        if (_current == loader.name) _current = null;
    }

    public void TaskRefreshed(Loading.LoaderBase loader) => Track(loader);

    public void RefreshDownloadButton()
    {
    }

    public void UpdateButtonProgress(double progress) => CliConsole.Progress("进度", progress * 100);

    public void UpdateTaskbar(TaskbarProgressState state, double progress)
    {
    }

    private void Track(Loading.LoaderBase loader)
    {
        _current = loader.name;
        if (loader.State == LoadState.Finished) CliConsole.ProgressDone();
    }
}
