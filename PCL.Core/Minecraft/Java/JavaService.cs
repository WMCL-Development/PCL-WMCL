using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App.IoC;
using PCL.Core.Minecraft.Java;
using PCL.Core.Minecraft.Java.Scanner;
using PCL.Core.Platform;

namespace PCL.Core.Minecraft;

[LifecycleService(LifecycleState.Loaded)]
[LifecycleScope("java", "Java 管理")]
public sealed partial class JavaService
{
    private static readonly Lock StartLock = new();
    private static JavaManager? _javaManager;
    private static Task<JavaManager>? _startTask;

    /// <summary>
    /// The manager, starting it on demand. Callers that can await should use
    /// <see cref="EnsureStartedAsync" />; this keeps the many synchronous call sites working
    /// even when the lifecycle scope has not finished yet.
    /// </summary>
    public static JavaManager JavaManager => _javaManager!;

    /// <summary>
    /// Returns the manager, starting it on first use. The lifecycle scope begins asynchronously,
    /// so anything that needs the manager must await this instead of assuming it is ready: on a
    /// phone the settings page can open long before the scan finishes.
    /// </summary>
    public static Task<JavaManager> EnsureStartedAsync()
    {
        lock (StartLock)
            return _startTask ??= StartCoreAsync();
    }

    [LifecycleStart]
    private static Task _StartAsync() => EnsureStartedAsync();

    private static async Task<JavaManager> StartCoreAsync()
    {
        if (_javaManager is not null) return _javaManager;

        Context.Info("Initializing Java Manager...");

        _javaManager = new JavaManager(
            ApplicationRuntime.Current.Platform.Java.CreateParser(),
            _CreateScanners());
        _javaManager.ReadConfig();

        Context.Info("Lookup for local Java...");
        await _javaManager.ScanJavaAsync();

        var logInfo = string.Join("\n\t", _javaManager.GetSortedJavaList());
        Context.Info($"Finished to scan java: \n\t{logInfo}");
        return _javaManager;
    }

    private static IJavaScanner[] _CreateScanners()
    {
        return ApplicationRuntime.Current.Platform.Java.CreateScanners();
    }

    [LifecycleStop]
    private static void _Stop()
    {
        if (_javaManager is null) return;

        _javaManager.SaveConfig();
        _javaManager = null;
        lock (StartLock)
            _startTask = null;
    }
}