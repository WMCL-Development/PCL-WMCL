using Android.Content;
using Android.OS;
using System.Runtime.InteropServices;

namespace PCL.Android;

internal static class AndroidJavaPaths
{
    public const string ProcessSuffix = ":jvm";

    public static bool IsJvmProcess => (Build.VERSION.SdkInt >= BuildVersionCodes.P
            ? global::Android.App.Application.ProcessName
            : File.ReadAllText("/proc/self/cmdline").Split('\0', 2)[0])
        ?.EndsWith(ProcessSuffix, StringComparison.Ordinal) == true;

    public static bool IsSupported => RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    public static string Root(Context context) => Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "pcl");
    public static string Runtimes(Context context) => Path.Combine(Root(context), "java");
    public static string Home(Context context) => Path.Combine(Root(context), "java-home");
    public static string Diagnostics(Context context) => Path.Combine(context.FilesDir!.AbsolutePath, "pcl", "log", "java");
    public static string Log(Context context) => Path.Combine(Diagnostics(context), "latestlog.txt");
    public static string ResultFile(Context context, Guid id) => Path.Combine(Root(context), $"jvm-{id:N}.result");
}
