using Android.Content;

namespace PCL.Android;

internal static class AndroidLwjglProbe
{
    /// <summary>Desktop-opengl probe: the same API surface Minecraft renders with.</summary>
    public const string MainClass = "WmclDesktopGlProbe";
    private const string ProbeResource = "WmclDesktopGlProbe.class";
    private static readonly (string Resource, string Name)[] Libraries =
    [
        ("wmcl.lwjgl-3.3.3.jar", "lwjgl-3.3.3.jar"),
        ("wmcl.lwjgl-glfw-3.3.3.jar", "lwjgl-glfw-3.3.3.jar"),
        ("wmcl.lwjgl-opengl-3.3.3.jar", "lwjgl-opengl-3.3.3.jar")
    ];

    public static bool IsAvailable => typeof(AndroidLwjglProbe).Assembly
        .GetManifestResourceInfo(Libraries[^1].Resource) is not null;

    public static async Task<JavaLaunchPlan> PrepareAsync(Context context, CancellationToken token)
    {
        var directory = Path.Combine(context.CacheDir!.AbsolutePath, "lwjgl-probe");
        var nativeDirectory = context.ApplicationInfo?.NativeLibraryDir
            ?? throw new InvalidOperationException("Android native library directory is unavailable.");
        foreach (var library in new[] { "liblwjgl.so", "libwmcl_glfw.so", "libmobileglues.so" })
            if (!File.Exists(Path.Combine(nativeDirectory, library)))
                throw new FileNotFoundException("The opt-in GL probe library was not packaged.", library);

        Directory.CreateDirectory(directory);
        var jars = new List<string>(Libraries.Length);
        foreach (var (resource, name) in Libraries)
        {
            var path = Path.Combine(directory, name);
            await ExportAsync(resource, path, token);
            jars.Add(path);
        }

        await ExportAsync(ProbeResource, Path.Combine(directory, ProbeResource), token);
        return new JavaLaunchPlan(
            ["-Xms16m", "-Xmx256m", "-Djava.awt.headless=true",
                // GL.<clinit> resolves liblwjgl_opengl.so through java.library.path only;
                // loadNative() (stb, openal, glfw) uses org.lwjgl.librarypath.
                $"-Djava.library.path={nativeDirectory}",
                $"-Dorg.lwjgl.librarypath={nativeDirectory}",
                // Stock LWJGL GL/GLFW bindings must resolve WMCL's shim and the GL translator.
                $"-Dorg.lwjgl.glfw.libname={Path.Combine(nativeDirectory, "libwmcl_glfw.so")}",
                // GL.create() tries libGLX.so.0 first and aborts, so the translator must be named.
                $"-Dorg.lwjgl.opengl.libname={Path.Combine(nativeDirectory, "libGL.so")}"],
            MainClass, [], directory, classPathEntries: [directory, .. jars]);
    }

    private static async Task ExportAsync(string resource, string path, CancellationToken token)
    {
        using var input = typeof(AndroidLwjglProbe).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The opt-in GL probe resources were not packaged.");
        await using var output = File.Create(path);
        await input.CopyToAsync(output, token);
    }
}
