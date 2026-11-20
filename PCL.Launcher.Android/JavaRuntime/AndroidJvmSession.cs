using Android.Content;
using Android.OS;
using PCL.Core.IO.Net;
using OperationCanceledException = System.OperationCanceledException;

namespace PCL.Android;

/// <summary>主进程通过 Binder 启动一次 HotSpot，结果在 JVM 进程退出后才算完成。</summary>
internal static class AndroidJvmSession
{
    internal const int StartMessage = 1;
    internal const int StopMessage = 2;
    internal const int TouchMessage = 3;
    internal const int FrameMessage = 4;
    internal const int ResizeMessage = 5;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim LaunchGate = new(1, 1);

    public static async Task RunAsync(Context context, JavaRuntimePackage package, JavaLaunchPlan plan,
        IProgress<JavaInstallProgress>? installProgress, Action? onStarting, TimeSpan? runTimeout,
        CancellationToken cancellationToken, AndroidLwjglWindow? window = null)
    {
        if (!AndroidJavaPaths.IsSupported)
            throw new PlatformNotSupportedException("目前只有 arm64-v8a 的 Android JDK 发行包。");

        await LaunchGate.WaitAsync(cancellationToken);
        try
        {
            var root = AndroidJavaPaths.Runtimes(context);
            if (JavaRuntimeInstaller.FindInstalled(root, package) is null)
            {
                using var client = NetworkService.GetClient();
                client.Timeout = Timeout.InfiniteTimeSpan;
                var installer = new JavaRuntimeInstaller(root, client);
                await installer.EnsureInstalledAsync(package, installProgress, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            onStarting?.Invoke();
            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (runTimeout is { } timeout)
                runCancellation.CancelAfter(timeout);
            try
            {
                await RunOnceAsync(context, package, plan, runCancellation.Token, window);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("等待 Android JVM 退出超时。", ex);
            }
        }
        finally
        {
            LaunchGate.Release();
        }
    }

    private static async Task RunOnceAsync(Context context, JavaRuntimePackage package, JavaLaunchPlan plan,
        CancellationToken token, AndroidLwjglWindow? window)
    {
        var id = Guid.NewGuid();
        var resultFile = AndroidJavaPaths.ResultFile(context, id);
        var launchLog = AndroidJavaPaths.Log(context);
        using var request = new Bundle();
        var session = id.ToString("N");
        request.PutString("id", session);
        request.PutInt("major", package.Major);
        request.PutString("workDir", plan.WorkingDirectory);
        request.PutString("mainClass", plan.MainClass);
        request.PutStringArray("jvmOptions", AndroidJavaArguments.BuildVmOptions(plan));
        request.PutStringArray("arguments", plan.GameArguments.ToArray());
        using var replyHandler = window is null ? null : new FrameHandler(window);
        using var reply = replyHandler is null ? null : new Messenger(replyHandler);
        if (window is not null)
            request.PutParcelable("surface", window.Surface);
        using var connection = new JvmConnection();
        using var intent = new Intent(context, typeof(AndroidJvmService));
        if (!context.BindService(intent, connection, Bind.AutoCreate))
            throw new InvalidOperationException("无法绑定 Android JVM 服务。");

        try
        {
            // A service that never connects would otherwise hang the launch with no signal.
            var binder = await connection.Connected.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            token.ThrowIfCancellationRequested();
            global::Android.Util.Log.Info("WMCL.PHASE", "service connected");
            using var messenger = new Messenger(binder);
            Send(messenger, StartMessage, request, reply);
            void SendTouch(int x, int y, bool pressed)
            {
                using var touch = new Bundle();
                touch.PutString("id", session);
                touch.PutInt("x", x);
                touch.PutInt("y", y);
                touch.PutInt("press", pressed ? 1 : 0);
                try { Send(messenger, TouchMessage, touch); }
                catch (RemoteException) { /* Process death is observed through the connection. */ }
            }
            void SendResize(int width, int height)
            {
                using var resize = new Bundle();
                resize.PutString("id", session);
                resize.PutInt("width", width);
                resize.PutInt("height", height);
                try { Send(messenger, ResizeMessage, resize); }
                catch (RemoteException) { /* Process death is observed through the connection. */ }
            }
            if (window is not null)
            {
                window.Touched += SendTouch;
                window.Resized += SendResize;
            }
            try
            {
                await connection.Exited.Task.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                if (!connection.Exited.Task.IsCompleted)
                {
                    using var stop = new Bundle();
                    stop.PutString("id", session);
                    try
                    {
                        Send(messenger, StopMessage, stop);
                        await connection.Exited.Task.WaitAsync(StopTimeout);
                    }
                    catch (Exception ex) when (ex is RemoteException or TimeoutException)
                    {
                        // If the process died first or Binder is stalled, unbinding is the fallback.
                    }
                }
                throw;
            }
            finally
            {
                if (window is not null)
                {
                    window.Touched -= SendTouch;
                    window.Resized -= SendResize;
                }
            }

            if (!File.Exists(resultFile))
                throw new InvalidOperationException($"JVM 进程异常退出，请查看 {launchLog}。");
            var status = await File.ReadAllTextAsync(resultFile, token);
            if (status != "OK")
                throw new InvalidOperationException($"{status}\nJVM 日志：{launchLog}");
        }
        finally
        {
            context.UnbindService(connection);
            File.Delete(resultFile);
        }
    }

    private static void Send(Messenger messenger, int what, Bundle data, Messenger? reply = null)
    {
        using var message = Message.Obtain() ?? throw new InvalidOperationException("无法创建 Android JVM 请求。");
        message.What = what;
        message.Data = data;
        if (reply is not null) message.ReplyTo = reply;
        messenger.Send(message);
    }

    private sealed class FrameHandler(AndroidLwjglWindow window) : Handler(Looper.MainLooper!)
    {
        public override void HandleMessage(Message message)
        {
            if (message.What == FrameMessage && message.SendingUid == global::Android.OS.Process.MyUid())
                window.FramePresented(message.Arg1, message.Arg2);
        }
    }

    private sealed class JvmConnection : Java.Lang.Object, IServiceConnection
    {
        public TaskCompletionSource<IBinder> Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnServiceConnected(ComponentName? name, IBinder? service)
        {
            if (service is null)
                OnNullBinding(name);
            else
                Connected.TrySetResult(service);
        }

        public void OnServiceDisconnected(ComponentName? name) => Complete();
        public void OnBindingDied(ComponentName? name) => Complete();
        public void OnNullBinding(ComponentName? name) =>
            Connected.TrySetException(new InvalidOperationException("Android JVM 服务未返回 Binder。"));

        private void Complete()
        {
            Connected.TrySetException(new InvalidOperationException("Android JVM 服务在启动前终止。"));
            Exited.TrySetResult();
        }
    }
}
