using System.Text;
using Android.Runtime;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.IO.Net;
using PCL.Core.Logging;
using PCL.Platform.Android;

namespace PCL.Android;

/// <summary>Android 运行时初始化：数据目录、配置、网络、日志与崩溃记录。</summary>
public static class AndroidBootstrap
{
    private static readonly StringBuilder _recent = new();
    private static readonly Lock _lock = new();
    private static bool _done;

    /// <summary>初始化失败时的完整错误信息。</summary>
    public static string? LastError { get; private set; }

    /// <summary>最近日志（供界面展示）。</summary>
    public static string RecentLog
    {
        get
        {
            lock (_lock) return _recent.ToString();
        }
    }

    /// <summary>应用私有目录下的日志文件。</summary>
    public static string LogFile => Path.Combine(DataRoot, "log", "android.log");

    /// <summary>外部可见的日志文件（/sdcard/Android/data/&lt;包名&gt;/files），便于导出排查。</summary>
    public static string? ExternalLogFile
    {
        get
        {
            try
            {
                var dir = global::Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
                return string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, "android.log");
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>应用私有数据目录，仅在外部目录不可用时作为回退存在。</summary>
    public static string DataRoot =>
        Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "pcl");

    public static void Initialize()
    {
        if (_done) return;
        _done = true;

        // 最先挂上崩溃钩子，保证后续任何异常都能落盘并弹出崩溃界面。
        CrashHooks.ReportCrash = AndroidCrashDialog.Show;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            WriteCrash("UnhandledException", e.ExceptionObject as Exception);
            if (e.ExceptionObject is Exception exception)
                AndroidCrashDialog.Show(exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash("UnobservedTaskException", e.Exception);
            AndroidCrashDialog.Show(e.Exception);
        };
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            WriteCrash("AndroidUnhandled", e.Exception);
            e.Handled = true;
            AndroidCrashDialog.Show(e.Exception);
        };

        // Android 精简运行时缺少 GB18030 等代码页，需要显式注册。
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        LogWrapper.OnLog += OnLog;
        Breadcrumb("bootstrap: 开始初始化");

        try
        {
            ConfigService.BeginRegistration();
            Breadcrumb("bootstrap: 配置项已登记");

            // Paths already resolved through PCL_ANDROID_ROOT, which AndroidPaths.Prepare points at
            // the external app directory when it is usable. Do not override it here: doing so
            // pinned every derived path (the Minecraft folder included) to app-private storage.
            Directory.CreateDirectory(Paths.Data);

            ConfigService.Initialize();
            Breadcrumb("bootstrap: 配置已加载");

            NetworkService.Initialize();
            Breadcrumb("bootstrap: 网络服务已就绪");

            LogWrapper.Info("Android", $"数据目录：{Paths.Data}");
        }
        catch (Exception exception)
        {
            RecordFatal("Bootstrap", exception);
        }
    }

    /// <summary>记录启动阶段错误，供界面展示。</summary>
    public static void RecordFatal(string source, Exception exception)
    {
        WriteCrash(source, exception);
        LastError = exception.ToString();
    }

    /// <summary>最近的启动/运行标记，供诊断文件使用。</summary>
    public static string RecentBreadcrumbs
    {
        get
        {
            lock (_lock) return _recent.ToString();
        }
    }

    /// <summary>记录一条启动进度，崩溃时用于定位卡在哪一步。</summary>
    public static void Breadcrumb(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [BC] {message}";
        lock (_lock)
        {
            _recent.AppendLine(line);
        }

        Append(line);
    }

    private static void OnLog(LogLevel level, string message, string? module, Exception? exception)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] [{module}] {message}" +
                   (exception is null ? "" : Environment.NewLine + exception);
        lock (_lock)
        {
            _recent.AppendLine(line);
            if (_recent.Length > 40000) _recent.Remove(0, _recent.Length - 32000);
        }

        Append(line);
    }

    private static void Append(string line)
    {
        foreach (var path in new[] { LogFile, ExternalLogFile })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // 写日志失败不影响主流程。
            }
        }
    }

    private static void WriteCrash(string source, Exception? exception)
    {
        if (exception is null) return;
        var text = $"[{DateTime.Now:O}] [CRASH] {source}{Environment.NewLine}{exception}";
        lock (_lock)
        {
            _recent.AppendLine(text);
        }

        Append(text);
        LastError = exception.ToString();
    }
}
