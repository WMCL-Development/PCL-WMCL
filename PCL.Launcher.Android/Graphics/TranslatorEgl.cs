using System.Runtime.InteropServices;

namespace PCL.Android;

/// <summary>
/// EGL entry points owned by the desktop-GL translator. A translator has to see the
/// context being created, so the window is built through these instead of Android's EGL14
/// when a translator is present.
/// </summary>
internal sealed unsafe class TranslatorEgl
{
    public const int DefaultDisplay = 0;
    public const int SurfaceType = 0x3033;
    public const int WindowBit = 0x0004;
    public const int RenderableType = 0x3040;
    public const int OpenGlEs3Bit = 0x0040;
    public const int RedSize = 0x3024;
    public const int GreenSize = 0x3023;
    public const int BlueSize = 0x3022;
    public const int ContextClientVersion = 0x3098;
    public const int Width = 0x3057;
    public const int Height = 0x3056;
    public const int None = 0x3038;
    public const int OpenGlEsApi = 0x30A0;

    private readonly nint _getDisplay;
    private readonly nint _initialize;
    private readonly nint _chooseConfig;
    private readonly nint _createContext;
    private readonly nint _createWindowSurface;
    private readonly nint _makeCurrent;
    private readonly nint _swapBuffers;
    private readonly nint _querySurface;
    private readonly nint _destroySurface;
    private readonly nint _destroyContext;
    private readonly nint _terminate;
    private readonly nint _releaseThread;
    private readonly nint _getProcAddress;
    private readonly nint _bindApi;

    private TranslatorEgl(nint library)
    {
        _getDisplay = Export(library, "eglGetDisplay");
        _initialize = Export(library, "eglInitialize");
        _chooseConfig = Export(library, "eglChooseConfig");
        _createContext = Export(library, "eglCreateContext");
        _createWindowSurface = Export(library, "eglCreateWindowSurface");
        _makeCurrent = Export(library, "eglMakeCurrent");
        _swapBuffers = Export(library, "eglSwapBuffers");
        _querySurface = Export(library, "eglQuerySurface");
        _destroySurface = Export(library, "eglDestroySurface");
        _destroyContext = Export(library, "eglDestroyContext");
        _terminate = Export(library, "eglTerminate");
        _releaseThread = Export(library, "eglReleaseThread");
        _getProcAddress = Export(library, "eglGetProcAddress");
        _bindApi = Export(library, "eglBindAPI");
    }

    /// <summary>Returns null when the library does not expose a usable EGL implementation.</summary>
    public static TranslatorEgl? TryCreate(nint library)
    {
        foreach (var name in new[]
                 {
                     "eglGetDisplay", "eglInitialize", "eglChooseConfig", "eglCreateContext",
                     "eglCreateWindowSurface", "eglMakeCurrent", "eglSwapBuffers", "eglQuerySurface"
                 })
            if (!NativeLibrary.TryGetExport(library, name, out _))
                return null;
        return new TranslatorEgl(library);
    }

    private static nint Export(nint library, string name) =>
        NativeLibrary.TryGetExport(library, name, out var address) ? address : 0;

    public nint GetDisplay() => ((delegate* unmanaged<int, nint>)_getDisplay)(DefaultDisplay);

    public bool Initialize(nint display) =>
        ((delegate* unmanaged<nint, int*, int*, int>)_initialize)(display, null, null) != 0;

    /// <summary>Selects the client API before a context exists; required on Android.</summary>
    public void BindOpenGlEs() =>
        ((delegate* unmanaged<int, int>)_bindApi)(OpenGlEsApi);

    public nint ChooseConfig(nint display, int[] attributes)
    {
        var configs = stackalloc nint[1];
        var count = 0;
        fixed (int* values = attributes)
            return ((delegate* unmanaged<nint, int*, nint*, int, int*, int>)_chooseConfig)(
                display, values, configs, 1, &count) != 0 && count > 0 ? configs[0] : 0;
    }

    public nint CreateContext(nint display, nint config, int[] attributes)
    {
        fixed (int* values = attributes)
            return ((delegate* unmanaged<nint, nint, nint, int*, nint>)_createContext)(
                display, config, 0, values);
    }

    /// <param name="window">ANativeWindow* for the Android Surface.</param>
    public nint CreateWindowSurface(nint display, nint config, nint window, int[] attributes)
    {
        fixed (int* values = attributes)
            return ((delegate* unmanaged<nint, nint, nint, int*, nint>)_createWindowSurface)(
                display, config, window, values);
    }

    public bool MakeCurrent(nint display, nint draw, nint read, nint context) =>
        ((delegate* unmanaged<nint, nint, nint, nint, int>)_makeCurrent)(display, draw, read, context) != 0;

    public bool SwapBuffers(nint display, nint surface) =>
        ((delegate* unmanaged<nint, nint, int>)_swapBuffers)(display, surface) != 0;

    public bool TryQuerySize(nint display, nint surface, out int width, out int height)
    {
        var value = 0;
        var ok = ((delegate* unmanaged<nint, nint, int, int*, int>)_querySurface)(display, surface, Width, &value) != 0;
        width = ok ? value : 0;
        ok = ((delegate* unmanaged<nint, nint, int, int*, int>)_querySurface)(display, surface, Height, &value) != 0;
        height = ok ? value : 0;
        return width > 0 && height > 0;
    }

    public void DestroySurface(nint display, nint surface)
    {
        if (_destroySurface != 0) ((delegate* unmanaged<nint, nint, int>)_destroySurface)(display, surface);
    }

    public void DestroyContext(nint display, nint context)
    {
        if (_destroyContext != 0) ((delegate* unmanaged<nint, nint, int>)_destroyContext)(display, context);
    }

    public void Terminate(nint display)
    {
        if (_terminate != 0) ((delegate* unmanaged<nint, int>)_terminate)(display);
        if (_releaseThread != 0) ((delegate* unmanaged<int>)_releaseThread)();
    }

    public nint GetProcAddress(string name)
    {
        if (_getProcAddress == 0) return 0;
        var utf8 = Marshal.StringToCoTaskMemUTF8(name);
        try { return ((delegate* unmanaged<nint, nint>)_getProcAddress)(utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }
}
