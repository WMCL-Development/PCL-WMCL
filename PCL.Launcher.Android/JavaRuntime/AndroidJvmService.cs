using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using System.Runtime.InteropServices;

namespace PCL.Android;

[Service(Exported = false, Process = AndroidJavaPaths.ProcessSuffix)]
public sealed class AndroidJvmService : Service
{
    private const string LogTag = "WMCL.JVM";
    private Messenger? _messenger;
    private Guid? _sessionId;
    private AndroidLwjglSurface? _graphics;

    public override IBinder? OnBind(Intent? intent)
    {
        if (!AndroidJavaPaths.IsJvmProcess)
            return null;
        _messenger ??= new Messenger(new LaunchHandler(this));
        return _messenger.Binder;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        // 即使绑定在 JVM 执行期间被撤销，下次也必须在全新进程启动 HotSpot。
        if (_sessionId.HasValue && AndroidJavaPaths.IsJvmProcess)
            Process.KillProcess(Process.MyPid());
        _messenger?.Dispose();
    }

    private void Start(Bundle data, Messenger? reply)
    {
        if (_sessionId.HasValue || !Guid.TryParseExact(data.GetString("id"), "N", out var id))
            return;

        // Copy data on the Binder handler, before Android recycles the message.
        var major = data.GetInt("major");
        var workingDirectory = data.GetString("workDir") ?? "";
        var mainClass = data.GetString("mainClass") ?? "";
        var options = data.GetStringArray("jvmOptions") ?? [];
        var arguments = data.GetStringArray("arguments") ?? [];
#pragma warning disable CS0618 // The Surface parcelable is supplied only by our own UID; support API 26.
        var surface = data.GetParcelable("surface") as Surface;
#pragma warning restore CS0618
        // Any Java main class may use the window; only a surface without a reply channel
        // cannot be supported, because frames and touches need a return path.
        if (surface is not null && reply is null)
        {
            surface.Dispose();
            return;
        }
        var graphics = surface is null ? null : new AndroidLwjglSurface(surface, (frames, touches) =>
        {
            using var frame = Message.Obtain();
            if (frame is null) return;
            frame.What = AndroidJvmSession.FrameMessage;
            frame.Arg1 = frames;
            frame.Arg2 = touches;
            reply!.Send(frame);
        });
        _graphics = graphics;
        _sessionId = id;
        Log.Info("WMCL.PHASE", $"service start main={mainClass} java={major} surface={(surface is not null)}");

        // JNI_CreateJavaVM, Java main and DestroyJavaVM must share this native thread.
        var thread = new Thread(() => Run(id, major, workingDirectory, mainClass, options, arguments, graphics))
        {
            IsBackground = true,
            Name = "WMCL HotSpot"
        };
        thread.Start();
    }

    private void Run(Guid id, int major, string workingDirectory, string mainClass, string[] options, string[] arguments,
        AndroidLwjglSurface? graphics)
    {
        var status = "OK";
        try
        {
            Directory.CreateDirectory(AndroidJavaPaths.Diagnostics(this));
            RedirectOutput(AndroidJavaPaths.Log(this));
            if (!AndroidJavaPaths.IsSupported)
                throw new PlatformNotSupportedException("Android JVM 当前支持 arm64-v8a。");
            var package = JavaRuntimePackage.Catalog.SingleOrDefault(candidate => candidate.Major == major)
                          ?? throw new InvalidOperationException("请求的 Java 版本没有 Android 发行包。");
            var runtime = JavaRuntimeInstaller.FindInstalled(AndroidJavaPaths.Runtimes(this), package)
                          ?? throw new InvalidOperationException("找不到已校验的 Android JDK。");

            var home = AndroidJavaPaths.Home(this);
            var temp = Path.Combine(CacheDir!.AbsolutePath, "jvm");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(temp);
            Directory.SetCurrentDirectory(workingDirectory);
            SetNativeEnvironment("JAVA_HOME", runtime.JavaHome);
            SetNativeEnvironment("HOME", home);
            SetNativeEnvironment("TMPDIR", temp);
            // The GL translator reads its own settings from this path; keep it app-private
            // instead of its default /sdcard/MG so the configuration is deterministic.
            var translatorSettings = Path.Combine(CacheDir!.AbsolutePath, "mobileglues");
            Directory.CreateDirectory(translatorSettings);
            SetNativeEnvironment("MG_DIR_PATH", translatorSettings);

            graphics?.Attach();

            AndroidHotSpot.Run(runtime,
                ["-Xrs", "-XX:-UsePerfData", "-XX:+DisableAttachMechanism", .. options,
                    $"-Djava.home={runtime.JavaHome}", $"-Djava.io.tmpdir={temp}", $"-Duser.home={home}",
                    $"-XX:ErrorFile={Path.Combine(AndroidJavaPaths.Diagnostics(this), "hs_err_pid%p.log")}"],
                mainClass, arguments);
        }
        catch (Exception exception)
        {
            status = exception.Message;
            Log.Error(LogTag, exception.ToString());
        }
        finally
        {
            try
            {
                graphics?.Dispose();
                FlushNative(0);
                File.WriteAllText(AndroidJavaPaths.ResultFile(this, id), status);
            }
            finally
            {
                // The caller observes Binder death after the result and HotSpot output are on disk.
                Process.KillProcess(Process.MyPid());
            }
        }
    }

    private static void RedirectOutput(string path)
    {
        using var output = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        var fd = (int)output.DangerousGetHandle();
        if (Dup2(fd, 1) == -1 || Dup2(fd, 2) == -1)
            throw new IOException($"无法重定向 JVM 输出（errno {Marshal.GetLastPInvokeError()}）。");
    }

    private static void SetNativeEnvironment(string key, string value)
    {
        // .NET 11 only updates its managed environment on Unix. HotSpot uses libc getenv.
        if (SetEnv(key, value, 1) != 0)
            throw new IOException($"无法设置 JVM 环境变量 {key}（errno {Marshal.GetLastPInvokeError()}）。");
    }

    [DllImport("libc.so", EntryPoint = "setenv", SetLastError = true)]
    private static extern int SetEnv([MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int overwrite);

    [DllImport("libc.so", EntryPoint = "dup2", SetLastError = true)]
    private static extern int Dup2(int oldFd, int newFd);

    [DllImport("libc.so", EntryPoint = "fflush")]
    private static extern int FlushNative(nint stream);

    private sealed class LaunchHandler(AndroidJvmService owner) : Handler(Looper.MainLooper!)
    {
        public override void HandleMessage(Message message)
        {
            if (message.SendingUid != Process.MyUid() || message.Data is not { } data)
                return;

            if (message.What == AndroidJvmSession.StartMessage)
                owner.Start(data, message.ReplyTo);
            else if (Guid.TryParseExact(data.GetString("id"), "N", out var id) && owner._sessionId == id)
            {
                if (message.What == AndroidJvmSession.StopMessage)
                {
                    if (owner._graphics is { } graphics) graphics.RequestClose();
                    else Process.KillProcess(Process.MyPid());
                }
                else if (message.What == AndroidJvmSession.TouchMessage)
                    owner._graphics?.Touch(data.GetInt("x"), data.GetInt("y"), data.GetInt("press") != 0);
                else if (message.What == AndroidJvmSession.ResizeMessage)
                    owner._graphics?.Resize(data.GetInt("width"), data.GetInt("height"));
            }
            else
                base.HandleMessage(message);
        }
    }
}
