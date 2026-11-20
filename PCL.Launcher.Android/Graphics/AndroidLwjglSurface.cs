using System.Runtime.InteropServices;
using Android.Opengl;
using Android.Runtime;
using Android.Util;
using Android.Views;

namespace PCL.Android;

/// <summary>
/// Owns the Android EGL window and exposes it to the official GLFW bindings through
/// WMCL's single-window ABI. Only one instance exists per :jvm process.
/// </summary>
internal sealed unsafe class AndroidLwjglSurface : IDisposable
{
    private const string LogTag = "WMCL.GLFW";
    private const string ShimName = "libwmcl_glfw.so";
    private const string TranslatorName = "libGL.so";
    private static AndroidLwjglSurface? _current;

    private readonly Surface _window;
    private readonly Action<int, int> _frame;
    private nint _shim;
    private nint _gles;
    private nint _translator;
    private TranslatorEgl? _egl;
    private nint _nativeWindow;
    private nint _display;
    private nint _surface;
    private nint _context;
    private int _touches;
    private int _frames;
    private int _lastFrame = -1;
    private bool _initialized;
    private bool _isCurrent;
    private bool _attached;
    private LegacyEgl _legacy;

    public AndroidLwjglSurface(Surface window, Action<int, int> frame)
    {
        _window = window;
        _frame = frame;
    }

    public int LastFrame => Volatile.Read(ref _lastFrame);

    /// <summary>Creates the GLES context, then hands it to the GLFW shim. Runs on the Java thread.</summary>
    public void Attach()
    {
        if (!_window.IsValid)
            throw new InvalidOperationException("Android Surface is no longer valid.");
        _gles = NativeLibrary.Load("libGLESv3.so");
        _shim = NativeLibrary.Load(ShimName);

        // A desktop-GL translator must create the context itself; Android's EGL14 would hide it.
        if (NativeLibrary.TryLoad(TranslatorName, out var translator))
        {
            _translator = translator;
            _egl = TranslatorEgl.TryCreate(translator);
        }

        int width, height;
        if (_egl is { } egl)
        {
            _nativeWindow = ANativeWindowFromSurface(JNIEnv.Handle, _window.Handle);
            if (_nativeWindow == 0) throw new InvalidOperationException("ANativeWindow_fromSurface failed.");
            _display = egl.GetDisplay();
            if (_display == 0 || !egl.Initialize(_display))
                throw new InvalidOperationException("Translator eglInitialize failed.");
            _initialized = true;
            egl.BindOpenGlEs();
            var config = egl.ChooseConfig(_display,
            [
                TranslatorEgl.SurfaceType, TranslatorEgl.WindowBit,
                TranslatorEgl.RenderableType, TranslatorEgl.OpenGlEs3Bit,
                TranslatorEgl.RedSize, 8, TranslatorEgl.GreenSize, 8, TranslatorEgl.BlueSize, 8,
                TranslatorEgl.None
            ]);
            if (config == 0) throw new InvalidOperationException("Translator eglChooseConfig failed.");
            _context = egl.CreateContext(_display, config,
                [TranslatorEgl.ContextClientVersion, 3, TranslatorEgl.None]);
            if (_context == 0) throw new InvalidOperationException("Translator eglCreateContext failed.");
            _surface = egl.CreateWindowSurface(_display, config, _nativeWindow, [TranslatorEgl.None]);
            if (_surface == 0) throw new InvalidOperationException("Translator eglCreateWindowSurface failed.");
            if (!egl.MakeCurrent(_display, _surface, _surface, _context))
                throw new InvalidOperationException("Translator eglMakeCurrent failed.");
            _isCurrent = true;
            if (!egl.TryQuerySize(_display, _surface, out width, out height))
            {
                width = 1280;
                height = 720;
            }
            Log.Info(LogTag, "translator EGL active");
        }
        else
        {
            var display = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
            if (display is null || display.Equals(EGL14.EglNoDisplay))
                throw new InvalidOperationException("eglGetDisplay failed.");
            if (!EGL14.EglInitialize(display, null, 0, null, 0))
                throw new InvalidOperationException("eglInitialize failed.");
            _initialized = true;
            var configs = new EGLConfig[1];
            var count = new int[1];
            if (!EGL14.EglChooseConfig(display,
                    [EGL14.EglSurfaceType, EGL14.EglWindowBit, EGL14.EglRenderableType, TranslatorEgl.OpenGlEs3Bit,
                        EGL14.EglRedSize, 8, EGL14.EglGreenSize, 8, EGL14.EglBlueSize, 8, EGL14.EglNone],
                    0, configs, 0, configs.Length, count, 0) || count[0] == 0)
                throw new InvalidOperationException("eglChooseConfig failed.");
            using var config = configs[0];
            var context = EGL14.EglCreateContext(display, config, EGL14.EglNoContext,
                [EGL14.EglContextClientVersion, 3, EGL14.EglNone], 0);
            if (context is null || context.Equals(EGL14.EglNoContext))
                throw new InvalidOperationException("eglCreateContext failed.");
            var surface = EGL14.EglCreateWindowSurface(display, config, _window, [EGL14.EglNone], 0);
            if (surface is null || surface.Equals(EGL14.EglNoSurface))
                throw new InvalidOperationException("eglCreateWindowSurface failed.");
            if (!EGL14.EglMakeCurrent(display, surface, surface, context))
                throw new InvalidOperationException("eglMakeCurrent failed.");
            _isCurrent = true;
            _legacy = new LegacyEgl { Display = display, Surface = surface, Context = context };
            var size = new int[2];
            width = EGL14.EglQuerySurface(display, surface, EGL14.EglWidth, size, 0) ? Math.Max(1, size[0]) : 1280;
            height = EGL14.EglQuerySurface(display, surface, EGL14.EglHeight, size, 1) ? Math.Max(1, size[1]) : 720;
            Log.Info(LogTag, "Android EGL14 active");
        }

        Volatile.Write(ref _current, this);
        var host = new GlfwHost
        {
            Width = width,
            Height = height,
            MakeCurrent = (nint)(delegate* unmanaged<int, int>)&MakeCurrent,
            SwapBuffers = (nint)(delegate* unmanaged<int>)&SwapBuffers,
            SwapInterval = (nint)(delegate* unmanaged<int, void>)&SwapInterval,
            GetProcAddress = (nint)(delegate* unmanaged<byte*, nint>)&GetProcAddress
        };
        var attach = (delegate* unmanaged<GlfwHost*, int>)NativeLibrary.GetExport(_shim, "wmcl_glfw_attach");
        _attached = attach(&host) != 0;
        if (!_attached) throw new InvalidOperationException("wmcl_glfw_attach failed.");
        Log.Info(LogTag, $"EGL ready {width}x{height}; GLFW shim attached");
    }

    private void Present()
    {
        var frames = Interlocked.Increment(ref _frames);
        Volatile.Write(ref _lastFrame, frames);
        try { _frame(frames, Volatile.Read(ref _touches)); }
        catch (Exception exception) { Log.Warn(LogTag, exception.Message); }
    }

    public void Touch(int x, int y, bool pressed)
    {
        if (!_attached || _shim == 0) return;
        var post = (delegate* unmanaged<double, double, int, int>)NativeLibrary.GetExport(_shim, "wmcl_glfw_post_touch");
        // The counter tracks taps, not GLFW press/release events.
        if (post(x, y, pressed ? 1 : 0) != 0 && pressed) Interlocked.Increment(ref _touches);
    }

    public void Resize(int width, int height)
    {
        if (!_attached || _shim == 0 || width < 1 || height < 1) return;
        var post = (delegate* unmanaged<int, int, int>)NativeLibrary.GetExport(_shim, "wmcl_glfw_post_resize");
        post(width, height);
    }

    public void RequestClose()
    {
        if (_attached && _shim != 0)
            ((delegate* unmanaged<void>)NativeLibrary.GetExport(_shim, "wmcl_glfw_request_close"))();
    }

    [UnmanagedCallersOnly]
    private static int MakeCurrent(int attach)
    {
        var current = Volatile.Read(ref _current);
        if (current is null) return 0;
        if (attach == 0)
        {
            if (!current._isCurrent) return 1;
            if (current._egl is { } egl) egl.MakeCurrent(current._display, 0, 0, 0);
            else EGL14.EglMakeCurrent(current._legacy.Display!, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
            current._isCurrent = false;
            return 1;
        }
        if (current._isCurrent) return 1;
        var ok = current._egl is { } builder
            ? builder.MakeCurrent(current._display, current._surface, current._surface, current._context)
            : EGL14.EglMakeCurrent(current._legacy.Display!, current._legacy.Surface!, current._legacy.Surface!,
                current._legacy.Context!);
        current._isCurrent = ok;
        return ok ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static int SwapBuffers()
    {
        var current = Volatile.Read(ref _current);
        if (current is null || !current._isCurrent) return 0;
        var ok = current._egl is { } egl
            ? egl.SwapBuffers(current._display, current._surface)
            : EGL14.EglSwapBuffers(current._legacy.Display!, current._legacy.Surface!);
        if (!ok) return 0;
        current.Present();
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void SwapInterval(int interval) { /* Android vsync follows the surface compositor. */ }

    [UnmanagedCallersOnly]
    private static nint GetProcAddress(byte* name)
    {
        var current = Volatile.Read(ref _current);
        if (current is null || name is null) return 0;
        var symbol = Marshal.PtrToStringUTF8((nint)name);
        if (symbol is null) return 0;
        if (current._egl is { } egl && egl.GetProcAddress(symbol) is var translated && translated != 0)
            return translated;
        return current._gles != 0 && NativeLibrary.TryGetExport(current._gles, symbol, out var address) ? address : 0;
    }

    public void Dispose()
    {
        RequestClose();
        Interlocked.CompareExchange(ref _current, null, this);
        if (_shim != 0)
        {
            ((delegate* unmanaged<void>)NativeLibrary.GetExport(_shim, "wmcl_glfw_detach"))();
            NativeLibrary.Free(_shim);
            _shim = 0;
        }
        if (_egl is { } egl)
        {
            if (_isCurrent) egl.MakeCurrent(_display, 0, 0, 0);
            if (_surface != 0) egl.DestroySurface(_display, _surface);
            if (_context != 0) egl.DestroyContext(_display, _context);
            if (_initialized) egl.Terminate(_display);
            _surface = _context = _display = 0;
        }
        else if (_legacy.Display is { } display && !display.Equals(EGL14.EglNoDisplay))
        {
            if (_isCurrent)
                EGL14.EglMakeCurrent(display, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
            if (_legacy.Surface is { } surface && !surface.Equals(EGL14.EglNoSurface))
                EGL14.EglDestroySurface(display, surface);
            if (_legacy.Context is { } context && !context.Equals(EGL14.EglNoContext))
                EGL14.EglDestroyContext(display, context);
            if (_initialized) EGL14.EglTerminate(display);
            EGL14.EglReleaseThread();
        }
        if (_translator != 0)
        {
            NativeLibrary.Free(_translator);
            _translator = 0;
        }
        if (_gles != 0)
        {
            NativeLibrary.Free(_gles);
            _gles = 0;
        }
        if (_nativeWindow != 0)
        {
            ANativeWindowRelease(_nativeWindow);
            _nativeWindow = 0;
        }
        _attached = false;
        _window.Dispose();
        Log.Info(LogTag, $"EGL released; frames={_frames} touches={_touches}");
    }

    private readonly struct LegacyEgl
    {
        public EGLDisplay? Display { get; init; }
        public EGLSurface? Surface { get; init; }
        public EGLContext? Context { get; init; }
    }

    [DllImport("libandroid.so", EntryPoint = "ANativeWindow_fromSurface")]
    private static extern nint ANativeWindowFromSurface(nint env, nint surface);

    [DllImport("libandroid.so", EntryPoint = "ANativeWindow_release")]
    private static extern void ANativeWindowRelease(nint window);

    [StructLayout(LayoutKind.Sequential)]
    private struct GlfwHost
    {
        public int Width;
        public int Height;
        public nint MakeCurrent;
        public nint SwapBuffers;
        public nint SwapInterval;
        public nint GetProcAddress;
    }
}
