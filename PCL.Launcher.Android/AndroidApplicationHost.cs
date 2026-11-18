using PCL.Core.Platform;

namespace PCL.Android;

/// <summary>
///     Android 的应用宿主：桌面端用 Avalonia 桌面生命周期阻塞 Run()，
///     Android 的界面循环由 Avalonia 自己持有，这里改为阻塞到应用退出信号。
/// </summary>
public sealed class AndroidApplicationHost : IApplicationHost
{
    private readonly ManualResetEventSlim _exit = new(false);
    private readonly Dictionary<object, object?> _resources = new();

    public AndroidApplicationHost()
    {
        MainWindow = new AndroidWindowHost();
    }

    public IWindowHost? MainWindow { get; set; }

    public int Run()
    {
        // 阻塞直到 MainActivity 销毁或显式关闭，保证生命周期状态不被提前拆除。
        _exit.Wait();
        return 0;
    }

    public object? TryFindResource(string key)
    {
        if (_resources.TryGetValue(key, out var cached)) return cached;
        return Avalonia.Application.Current?.Resources.TryGetResource(key, null, out var value) == true
            ? value
            : null;
    }

    public object? FindResource(string key) =>
        TryFindResource(key) ?? throw new KeyNotFoundException($"未找到资源：{key}");

    public void SetResource(object key, object value)
    {
        _resources[key] = value;
        if (Avalonia.Application.Current is { } application)
            application.Resources[key] = value;
    }

    public void Shutdown() => _exit.Set();

    public event Action? Startup;

    public event Action<Exception>? DispatcherUnhandledException;

    /// <summary>通知应用已启动（生命周期 Running 阶段）。</summary>
    public void NotifyStarted() => Startup?.Invoke();

    /// <summary>上报未处理异常（生命周期会按致命错误处理）。</summary>
    public void NotifyUnhandled(Exception exception) => DispatcherUnhandledException?.Invoke(exception);
}

/// <summary>Android 没有桌面窗口，这里提供空实现以满足生命周期调用。</summary>
public sealed class AndroidWindowHost : IWindowHost
{
    public bool IsMinimized => false;

    public bool IsTopmost { get; set; }

    public event Action? Loaded;

    public void Show() => Loaded?.Invoke();

    public void Activate()
    {
    }

    public void Restore()
    {
    }
}
