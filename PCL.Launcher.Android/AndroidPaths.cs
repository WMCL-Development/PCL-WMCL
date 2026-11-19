namespace PCL.Android;

/// <summary>
///     Android 数据目录解析：优先使用外部可见目录
///     (/storage/emulated/0/Android/data/&lt;包名&gt;/files)，用户可用文件管理器直接访问；
///     外部存储不可用时回退到应用私有目录。
/// </summary>
public static class AndroidPaths
{
    /// <summary>必须在任何 PCL.Core 类型被触碰前调用。</summary>
    public static void Prepare()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var external = context.GetExternalFilesDir(null)?.AbsolutePath;
            var root = !string.IsNullOrWhiteSpace(external) && Directory.Exists(external)
                ? Path.Combine(external, "pcl")
                : Path.Combine(context.FilesDir!.AbsolutePath, "pcl");

            Directory.CreateDirectory(root);
            Environment.SetEnvironmentVariable("PCL_ANDROID_ROOT", root);

            // EasyTier 的 core/cli 以 lib*.so 随 APK 分发在 nativeLibraryDir，
            // 该目录允许执行；shared-local 里的路径在 Android 上既不存在也不能执行。
            var nativeDir = context.ApplicationInfo?.NativeLibraryDir;
            if (!string.IsNullOrWhiteSpace(nativeDir))
            {
                foreach (var (env, file) in new[]
                         {
                             ("PCL_EASYTIER_CORE_PATH", "libeasytier-core.so"),
                             ("PCL_EASYTIER_CLI_PATH", "libeasytier-cli.so")
                         })
                {
                    var candidate = Path.Combine(nativeDir, file);
                    if (File.Exists(candidate))
                        Environment.SetEnvironmentVariable(env, candidate);
                }
            }
        }
        catch (Exception)
        {
            // 解析失败时让 Paths 走默认回退逻辑。
        }
    }
}
