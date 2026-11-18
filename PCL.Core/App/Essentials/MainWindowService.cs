using System;
using PCL.Core.App.IoC;
using PCL.Core.Platform;

namespace PCL.Core.App.Essentials;

[LifecycleService(LifecycleState.WindowCreating, Priority = int.MaxValue)]
public sealed class MainWindowService : GeneralService
{
    private static LifecycleContext? _context;

    private MainWindowService() : base("window", "主窗体", false)
    {
        _context = ServiceContext;
    }

    public static Func<IWindowHost>? Loading { private get; set; }
    private static LifecycleContext Context => _context!;

    public override void Start()
    {
        // Android 使用单视图宿主，没有桌面窗口；窗口阶段由宿主自行推进。
        if (OperatingSystem.IsAndroid() || Loading is null)
        {
            Context.Debug("当前平台没有桌面窗口，跳过窗体创建");
            return;
        }

        Context.Debug("正在初始化应用程序窗体");
        var window = Loading!.Invoke();
        window.Loaded += Lifecycle.OnWindowCreated;
        Lifecycle.CurrentApplication.MainWindow = window;
        Context.Trace("窗体创建完毕");
    }
}