using PCL.Core.Minecraft.Java;

namespace PCL;

public static partial class GameLaunch
{
    /// <summary>
    /// Android keeps its natives inside the APK, so the desktop extraction step has no
    /// meaning there. When set, this replaces the natives folder the launch plan and the
    /// extraction step use.
    /// </summary>
    public static Func<string>? PlatformNativesFolder { get; set; }

    /// <summary>
    /// Replaces the desktop tail of the launch chain (extract natives, pre-launch, start
    /// process, wait for the window). A platform that cannot spawn a Java process supplies
    /// its own loaders here; everything before it — file downloads, authentication and the
    /// argument builder — stays the launcher's existing implementation.
    /// </summary>
    public static Func<List<Loading.LoaderBase>>? PlatformLaunchLoaders { get; set; }

    /// <summary>The plan produced by the argument step, for a platform-owned tail.</summary>
    public static JavaLaunchPlan? CurrentLaunchPlan => _mcLaunchPlan;

    /// <summary>
    /// Selects the Java installation the argument builders should assume. Android hosts a
    /// downloaded runtime, so it supplies this instead of scanning the host system.
    /// </summary>
    public static void SelectJava(JavaEntry entry) => mcLaunchJavaSelected = entry;
}
