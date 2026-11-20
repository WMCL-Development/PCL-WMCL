using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using CancellationException = System.OperationCanceledException;

namespace PCL.Android;

// The launcher owns the view. The non-exported :jvm service receives the Surface via Binder.
[Activity(Exported = false, Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class AndroidLwjglProbeActivity : Activity
{
    private TouchView? _view;
    private TextView? _status;
    private SurfaceCallback? _callback;
    private CancellationTokenSource? _run;
    private AndroidLwjglWindow? _window;
    private bool _resumed;
    private bool _surfaceReady;
    private bool _retryAfterStop;
    private Mode _mode;

    private enum Mode { Graphics, Vanilla }

    /// <param name="vanilla">True launches the real Minecraft client instead of the GL triangle.</param>
    public static void Open(Context context, bool vanilla = false)
    {
        using var intent = new Intent(context, typeof(AndroidLwjglProbeActivity));
        intent.PutExtra("vanilla", vanilla);
        if (context is not Activity) intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        _mode = Intent?.GetBooleanExtra("vanilla", false) == true ? Mode.Vanilla : Mode.Graphics;
        if (!AndroidJavaPaths.IsSupported || !AndroidLwjglProbe.IsAvailable || AndroidJavaPaths.IsJvmProcess)
        {
            Finish();
            return;
        }
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetFitsSystemWindows(true);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            root.SetOnApplyWindowInsetsListener(new SafeArea());
        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetBackgroundColor(global::Android.Graphics.Color.Rgb(29, 40, 54));
        var back = new Button(this) { Text = "Back" };
        back.Click += (_, _) => Finish();
        _status = new TextView(this) { Text = "Waiting for the Java/LWJGL frame…", TextSize = 14 };
        _status.SetTextColor(global::Android.Graphics.Color.White);
        _status.SetSingleLine(true);
        _status.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        bar.AddView(back);
        bar.AddView(_status, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        var density = Resources?.DisplayMetrics?.Density ?? 1;
        root.AddView(bar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, (int)Math.Round(52 * density)));
        _view = new TouchView(this, (x, y, pressed) => _window?.PushTouch(x, y, pressed));
        _callback = new SurfaceCallback(this);
        _view.Holder?.AddCallback(_callback);
        root.AddView(_view, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        SetContentView(root);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _resumed = true;
        TryStart();
    }

    protected override void OnPause()
    {
        _resumed = false;
        _run?.Cancel();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _run?.Cancel();
        if (_callback is { } callback)
        {
            _view?.Holder?.RemoveCallback(callback);
            callback.Dispose();
        }
        _status = null;
        base.OnDestroy();
    }

    private async void TryStart()
    {
        if (!_resumed || !_surfaceReady || IsFinishing || IsDestroyed ||
            _view?.Holder?.Surface is not { IsValid: true } surface)
            return;
        if (_run is { } running)
        {
            if (running.IsCancellationRequested) _retryAfterStop = true;
            return;
        }

        using var operation = new CancellationTokenSource();
        _run = operation;
        _retryAfterStop = false;
        var vanilla = _mode == Mode.Vanilla;
        var window = new AndroidLwjglWindow(surface, (frames, touches) =>
        {
            if (_run == operation && !operation.IsCancellationRequested)
                ShowStatus(vanilla
                    ? $"WMCL_MC_OK · frame {frames} · touch {touches}"
                    : $"WMCL_GL_OK · frame {frames} · touch {touches}");
        });
        _window = window;
        try
        {
            ShowStatus(vanilla ? "Preparing Minecraft 1.21.1…" : "Preparing Java 21 / LWJGL…");
            Phase(vanilla ? "prepare vanilla" : "prepare probe");
            var plan = vanilla
                ? await AndroidVanillaLauncher.PrepareAsync(this, Math.Max(1, _view?.Width ?? 1280),
                    Math.Max(1, _view?.Height ?? 720),
                    new Progress<AndroidVanillaProgress>(progress =>
                    {
                        if (_run != operation || operation.IsCancellationRequested) return;
                        ShowStatus($"{progress.Step}: {progress.Completed}/{progress.Total}");
                        Phase($"{progress.Step} {progress.Completed}/{progress.Total}");
                    }), operation.Token)
                : await AndroidLwjglProbe.PrepareAsync(this, operation.Token);
            Phase("plan ready");
            var runtime = JavaRuntimePackage.Catalog.Single(package => package.Major == 21);
            await AndroidJvmSession.RunAsync(this, runtime, plan,
                new Progress<JavaInstallProgress>(progress =>
                {
                    if (_run == operation && !operation.IsCancellationRequested)
                        ShowStatus($"Java 21: {progress.Stage}");
                }), () => ShowStatus(vanilla ? "Starting Minecraft…" : "Starting the Java/LWJGL renderer…"),
                TimeSpan.FromMinutes(vanilla ? 20 : 3), operation.Token, window);
        }
        catch (CancellationException) when (operation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("WMCL.LWJGL", exception.ToString());
            ShowStatus($"WMCL_LWJGL_FAILED · {exception.Message.Replace('\n', ' ')}");
        }
        finally
        {
            _window = null;
            _run = null;
            if (_retryAfterStop) TryStart();
        }
    }

    /// <summary>Milestones go to logcat as well, because a frozen process loses no log.</summary>
    private void Phase(string value)
    {
        global::Android.Util.Log.Info("WMCL.PHASE", value);
    }

    private void ShowStatus(string value)
    {
        if (_status is { } status && !IsDestroyed) status.Text = value;
    }

    private sealed class SurfaceCallback(AndroidLwjglProbeActivity activity)
        : Java.Lang.Object, ISurfaceHolderCallback
    {
        public void SurfaceCreated(ISurfaceHolder holder) { }
        public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
        {
            activity._surfaceReady = width > 0 && height > 0;
            activity._window?.Resize(width, height);
            activity.TryStart();
        }
        public void SurfaceDestroyed(ISurfaceHolder holder)
        {
            activity._surfaceReady = false;
            activity._run?.Cancel();
        }
    }

    private sealed class SafeArea : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            using var safe = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            if (safe is not null && (view.PaddingLeft != safe.Left || view.PaddingTop != safe.Top ||
                view.PaddingRight != safe.Right || view.PaddingBottom != safe.Bottom))
                view.SetPadding(safe.Left, safe.Top, safe.Right, safe.Bottom);
            return insets;
        }
    }

    private sealed class TouchView(Context context, Action<int, int, bool> touch) : SurfaceView(context)
    {
        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (Width <= 0 || Height <= 0 || e is null) return true;
            if (e.ActionMasked is MotionEventActions.Down or MotionEventActions.Up)
                touch((int)e.GetX(), (int)e.GetY(), e.ActionMasked == MotionEventActions.Down);
            if (e.ActionMasked == MotionEventActions.Up) PerformClick();
            return true;
        }

        public override bool PerformClick()
        {
            base.PerformClick();
            return true;
        }
    }
}
