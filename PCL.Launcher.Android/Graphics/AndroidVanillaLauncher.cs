using Android.Content;
using PCL.Core.IO.Net;

namespace PCL.Android;

/// <summary>
/// Prepares the pinned vanilla 1.21.1 files in app cache and builds the real client argv.
/// The probe account only reaches the main menu; it is not a signed-in session.
/// </summary>
internal static class AndroidVanillaLauncher
{
    private const string ProbePlayer = "WMCLProbe";
    private static readonly Guid ProbeUuid = Guid.Parse("4c9a1f6e-3b2e-4f28-9a11-5d6f7a8b9c0d");

    public static async Task<JavaLaunchPlan> PrepareAsync(Context context, int width, int height,
        IProgress<AndroidVanillaProgress>? progress, CancellationToken token)
    {
        var nativeDirectory = context.ApplicationInfo?.NativeLibraryDir
            ?? throw new InvalidOperationException("Android native library directory is unavailable.");
        var root = Path.Combine(context.CacheDir!.AbsolutePath, "vanilla");
        using var client = NetworkService.GetClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        var metadata = await new AndroidVanillaDownloader(client).PrepareAsync(root, progress, token);
        var plan = metadata.MakeLaunchPlan(ProbePlayer, ProbeUuid, "0", "0", "0", nativeDirectory,
            width, height, 768, "wmcl-probe");

        // The vanilla plan points LWJGL at the desktop native jars; Android uses WMCL's own.
        // GL.create() aborts on the first candidate name it cannot load, so the translator
        // is addressed explicitly instead of relying on libGLX.so.0 / libGL.so.1 probing.
        var jvm = plan.JvmOptions.ToList();
        jvm.Add($"-Djava.library.path={nativeDirectory}");
        jvm.Add($"-Dorg.lwjgl.librarypath={nativeDirectory}");
        jvm.Add($"-Dorg.lwjgl.opengl.libname={Path.Combine(nativeDirectory, "libGL.so")}");
        jvm.Add($"-Dorg.lwjgl.glfw.libname={Path.Combine(nativeDirectory, "libwmcl_glfw.so")}");
        return new JavaLaunchPlan(jvm, plan.MainClass, plan.GameArguments, plan.WorkingDirectory,
            plan.NativesDirectory, plan.ClassPathEntries);
    }
}
