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
            // GetExternalFilesDir hands back a path whose directory may not exist yet on the
            // first frame after install, so create it instead of testing for it: testing sent
            // the whole launcher to app-private storage and that choice then stuck, because the
            // selected folder is persisted.
            var root = CreateRoot(context.GetExternalFilesDir(null)?.AbsolutePath)
                       ?? CreateRoot(context.FilesDir!.AbsolutePath);
            if (root is null)
                return;

            Environment.SetEnvironmentVariable("PCL_ANDROID_ROOT", root);
            global::Android.Util.Log.Info("WMCL.PATHS", "数据目录：" + root);

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

    /// <summary>Creates and returns `&lt;base&gt;/pcl`, or null when that location is unusable.</summary>
    private static string? CreateRoot(string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return null;
        try
        {
            var root = Path.Combine(basePath, "pcl");
            Directory.CreateDirectory(root);
            return root;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            global::Android.Util.Log.Warn("WMCL.PATHS", $"无法创建 {basePath}/pcl：{exception.Message}");
            return null;
        }
    }
}
