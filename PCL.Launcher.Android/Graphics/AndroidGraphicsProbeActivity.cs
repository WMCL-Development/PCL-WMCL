using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.Opengl;
using Android.OS;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using Java.Nio;
using Javax.Microedition.Khronos.Opengles;
using EglConfig = Javax.Microedition.Khronos.Egl.EGLConfig;
using View = Android.Views.View;

namespace PCL.Android;

/// <summary>
/// Checks window graphics and touch in the JVM host process without starting HotSpot.
/// The chrome is a fixed-height strip that never overlaps the EGL surface: the platform
/// action bar is removed, the root fits the system bars, and the status text cannot
/// change the layout height (which previously resized the surface in a feedback loop).
/// </summary>
[Activity(Exported = false, Process = AndroidJavaPaths.ProcessSuffix,
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class AndroidGraphicsProbeActivity : Activity
{
    private const string LogTag = "WMCL.Graphics";
    private const int HeaderHeightDip = 56;
    private const int StatusLineHeightDip = 24;
    private const int DetailLineHeightDip = 18;

    private ProbeView? _view;
    private TextView? _status;
    private TextView? _detail;
    private LinearLayout? _root;
    private View? _header;
    private string _probeTitle = "Surface / EGL";
    private Color _statusColor;
    private Color _failureColor;
    private bool _layoutLogged;

    public static void Open(Context context, string title)
    {
        using var intent = new Intent(context, typeof(AndroidGraphicsProbeActivity));
        intent.PutExtra("title", title);
        if (context is not Activity)
            intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!AndroidJavaPaths.IsJvmProcess || !AndroidJavaPaths.IsSupported)
        {
            Finish();
            return;
        }

        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        int Dip(float value) => (int)Math.Round(value * density);

        var background = Color.ParseColor("#FF0D131A");
        var headerBackground = Color.ParseColor("#FF1D2836");
        var accent = Color.ParseColor("#FF6FB8FF");
        var buttonText = Color.ParseColor("#FF08131E");
        _statusColor = Color.ParseColor("#FFF3F6FA");
        _failureColor = Color.ParseColor("#FFFF8A80");
        var detailText = Color.ParseColor("#FFA8B8CC");

        _probeTitle = Intent?.GetStringExtra("title") ?? "Surface / EGL";

        var root = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        root.SetBackgroundColor(background);
        // Keep the header and surface clear of system bars when the window is edge-to-edge.
        root.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener());
        _root = root;

        // Header: explicit height, opaque background, and its own back button. The status
        // lines below use fixed heights, so any text change is width-only and can never
        // change the EGL surface size.
        var header = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal };
        header.SetBackgroundColor(headerBackground);
        header.SetGravity(GravityFlags.CenterVertical);
        header.SetPadding(Dip(12), Dip(4), Dip(12), Dip(4));
        header.Elevation = Dip(6);
        _header = header;

        var back = new Button(this)
        {
            Text = "‹ Back",
            ContentDescription = "Close the OpenGL ES probe"
        };
        back.SetTextSize(ComplexUnitType.Sp, 14);
        back.SetAllCaps(false);
        back.SetTextColor(buttonText);
        back.BackgroundTintList = ColorStateList.ValueOf(accent);
        back.SetMinimumWidth(0);
        back.SetMinimumHeight(0);
        back.Click += (_, _) => Finish();
        var backParams = new LinearLayout.LayoutParams(Dip(104), Dip(38)) { RightMargin = Dip(12) };
        header.AddView(back, backParams);

        var column = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        column.SetGravity(GravityFlags.CenterVertical);

        var statusView = new TextView(this) { Text = "Waiting for the first OpenGL ES 3 frame…" };
        statusView.SetTextColor(_statusColor);
        statusView.SetTextSize(ComplexUnitType.Sp, 15);
        statusView.SetSingleLine(true);
        statusView.Ellipsize = TextUtils.TruncateAt.End;
        statusView.Gravity = GravityFlags.CenterVertical;
        statusView.Typeface = Typeface.DefaultBold;
        column.AddView(statusView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dip(StatusLineHeightDip)));
        _status = statusView;

        var detailView = new TextView(this) { Text = _probeTitle };
        detailView.SetTextColor(detailText);
        detailView.SetTextSize(ComplexUnitType.Sp, 12);
        detailView.SetSingleLine(true);
        detailView.Ellipsize = TextUtils.TruncateAt.End;
        detailView.Gravity = GravityFlags.CenterVertical;
        column.AddView(detailView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dip(DetailLineHeightDip)));
        _detail = detailView;

        header.AddView(column, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.MatchParent, 1));

        root.AddView(header, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dip(HeaderHeightDip)));

        var view = new ProbeView(this, Report);
        // The surface stays behind the window; the header occupies its own strip and is
        // never inside the surface bounds, so no compositor z-order can hide it.
        view.SetZOrderOnTop(false);
        view.SetZOrderMediaOverlay(false);
        root.AddView(view, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1));
        _view = view;

        SetContentView(root);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _view?.OnResume();
        _view?.RequestRender();
    }

    protected override void OnPause()
    {
        _view?.OnPause();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _status = null;
        _detail = null;
        _view = null;
        _root = null;
        _header = null;
        base.OnDestroy();
    }

    private void Report(string message)
    {
        // The log keeps the full, unmodified line; only the on-screen text is split up.
        Log.Info(LogTag, message);
        var (status, detail) = Describe(message);
        var failed = status.StartsWith("WMCL_EGL_FAILED", StringComparison.Ordinal);
        RunOnUiThread(() =>
        {
            if (IsFinishing || IsDestroyed)
                return;
            if (_status is { } statusView && !string.Equals(statusView.Text, status, StringComparison.Ordinal))
            {
                statusView.SetTextColor(failed ? _failureColor : _statusColor);
                statusView.Text = status;
            }
            if (_detail is { } detailView && !string.Equals(detailView.Text, detail, StringComparison.Ordinal))
                detailView.Text = detail;
            LogLayoutOnce();
        });
    }

    /// <summary>
    /// "WMCL_EGL_OK · GLES 3.2 · 3200×1206 · EGL #3 · Touch 10" becomes
    /// "WMCL_EGL_OK · Touch 10" plus "Surface / EGL · GLES 3.2 · 3200×1206 · EGL #3",
    /// so the counter survives truncation. Failure messages are shown verbatim.
    /// </summary>
    private (string Status, string Detail) Describe(string message)
    {
        var parts = message.Split(" · ", StringSplitOptions.None);
        if (parts.Length < 3)
            return (message, _probeTitle);
        return (parts[0] + " · " + parts[^1],
            _probeTitle + " · " + string.Join(" · ", parts[1..^1]));
    }

    private void LogLayoutOnce()
    {
        if (_layoutLogged)
            return;
        _layoutLogged = true;
        try
        {
            var insets = Window?.DecorView?.RootWindowInsets;
#pragma warning disable CS0618 // populated on every API level; used for this one log line
            var insetTop = insets?.SystemWindowInsetTop ?? -1;
#pragma warning restore CS0618
            Log.Info(LogTag,
                $"WMCL_UI_LAYOUT root={_root?.Width}×{_root?.Height} " +
                $"header={_header?.Top}..{_header?.Bottom} " +
                $"surface={_view?.Top}..{_view?.Bottom} ({_view?.Width}×{_view?.Height}) " +
                $"insetTop={insetTop} statusHeight={_status?.Height} detailHeight={_detail?.Height}");
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "WMCL_UI_LAYOUT failed: " + exception.Message);
        }
    }

    private sealed class SystemBarInsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                using var safe = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
                if (safe is not null)
                    ApplyPadding(view, safe.Left, safe.Top, safe.Right, safe.Bottom);
            }
            else
            {
#pragma warning disable CS0618 // Android 8–10 fallback
                ApplyPadding(view, insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop,
                    insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
#pragma warning restore CS0618
            }
            return insets;
        }

        private static void ApplyPadding(View view, int left, int top, int right, int bottom)
        {
            if (view.PaddingLeft != left || view.PaddingTop != top ||
                view.PaddingRight != right || view.PaddingBottom != bottom)
                view.SetPadding(left, top, right, bottom);
        }
    }

    private sealed class ProbeView : GLSurfaceView
    {
        private readonly ProbeRenderer _renderer;

        public ProbeView(Context context, Action<string> report) : base(context)
        {
            _renderer = new ProbeRenderer(report);
            SetEGLContextClientVersion(3);
            // Exercise context recreation as well as simple first-frame rendering.
            PreserveEGLContextOnPause = false;
            SetRenderer(_renderer);
            RenderMode = Rendermode.WhenDirty;
            Clickable = true;
            ContentDescription = "OpenGL ES probe; tap the colored surface";
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e is null)
                return base.OnTouchEvent(e);
            if (e.ActionMasked == MotionEventActions.Down && Width > 0 && Height > 0)
            {
                _renderer.SetTouch(e.GetX() / Width, e.GetY() / Height);
                RequestRender();
            }
            else if (e.ActionMasked == MotionEventActions.Up)
                PerformClick();
            return true;
        }

        public override bool PerformClick()
        {
            base.PerformClick();
            return true;
        }
    }

    // The UI produces normalized touch coordinates. Only the GL thread owns graphics state.
    private sealed class ProbeRenderer(Action<string> report) : Java.Lang.Object, GLSurfaceView.IRenderer
    {
        private sealed record Touch(float X, float Y, int Count);
        private Touch _touch = new(0, 0, 0);
        private int _contextGeneration;
        private int _width;
        private int _height;
        private int _lastReportedTouch = -1;
        private bool _verifyClear;
        private string? _version;

        public void SetTouch(float x, float y)
        {
            var previous = Volatile.Read(ref _touch);
            Volatile.Write(ref _touch, new Touch(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1), previous.Count + 1));
        }

        public void OnSurfaceCreated(IGL10? gl, EglConfig? config)
        {
            _contextGeneration++;
            _verifyClear = true;
            _lastReportedTouch = -1;
            _version = GLES20.GlGetString(GLES20.GlVersion);
        }

        public void OnSurfaceChanged(IGL10? gl, int width, int height)
        {
            _width = width;
            _height = height;
            _lastReportedTouch = -1;
            GLES20.GlViewport(0, 0, width, height);
        }

        public void OnDrawFrame(IGL10? gl)
        {
            if (_width <= 0 || _height <= 0)
                return;

            var touch = Volatile.Read(ref _touch);
            var changed = (touch.Count & 1) != 0;
            GLES20.GlClearColor(changed ? 0.56f : 0.09f, changed ? 0.20f : 0.34f, changed ? 0.12f : 0.65f, 1);
            GLES20.GlClear(GLES20.GlColorBufferBit);

            if (_verifyClear)
            {
                using var pixel = ByteBuffer.AllocateDirect(4)!;
                GLES20.GlReadPixels(_width / 2, _height / 2, 1, 1, GLES20.GlRgba, GLES20.GlUnsignedByte, pixel);
                var error = GLES20.GlGetError();
                var red = pixel.Get(0) & 0xff;
                var blue = pixel.Get(2) & 0xff;
                if (error != GLES20.GlNoError || (changed ? red <= blue : blue <= red))
                {
                    report($"WMCL_EGL_FAILED readback error=0x{error:x}, red={red}, blue={blue}");
                    return;
                }
                _verifyClear = false;
            }

            if (touch.Count > 0)
            {
                var size = Math.Min(_width, _height) / 9;
                var x = (int)(touch.X * _width) - size / 2;
                var y = (int)((1 - touch.Y) * _height) - size / 2;
                GLES20.GlEnable(GLES20.GlScissorTest);
                GLES20.GlScissor(x, y, size, size);
                GLES20.GlClearColor(1, 0.84f, 0.28f, 1);
                GLES20.GlClear(GLES20.GlColorBufferBit);
                GLES20.GlDisable(GLES20.GlScissorTest);
            }

            var drawError = GLES20.GlGetError();
            if (drawError != GLES20.GlNoError)
            {
                report($"WMCL_EGL_FAILED draw error=0x{drawError:x}");
                return;
            }

            if (_lastReportedTouch != touch.Count)
            {
                _lastReportedTouch = touch.Count;
                report($"WMCL_EGL_OK · {_version} · {_width}×{_height} · EGL #{_contextGeneration} · Touch {touch.Count}");
            }
        }
    }
}
