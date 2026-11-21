using PCL;

namespace PCL.Android;

/// <summary>
/// Downloads the Android runtime from the project's own JDK releases. The catalog pins every
/// archive by SHA-256 and the installer verifies before unpacking, so a broken transfer or a
/// mirror cannot silently produce a half-valid runtime.
/// </summary>
internal sealed class AndroidJavaRuntimeProvider : IJavaRuntimeProvider
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromMinutes(5) };

    public bool IsAvailable => true;

    public IReadOnlyList<string> Options { get; } =
        JavaRuntimePackage.Catalog.Select(package => package.ToString()).ToList();

    public async Task<string> InstallAsync(
        int optionIndex,
        Action<string, double> progress,
        CancellationToken token)
    {
        var package = JavaRuntimePackage.Catalog[optionIndex];
        progress("正在下载 " + package, 0);
        var root = AndroidJavaPaths.Runtimes(global::Android.App.Application.Context);
        var installer = new JavaRuntimeInstaller(root, _client);
        var installed = await installer.EnsureInstalledAsync(
            package,
            new Progress<JavaInstallProgress>(value => progress(
                value.Stage.ToString(),
                package.ArchiveSize == 0 ? 0 : Math.Clamp((double)value.Bytes / package.ArchiveSize, 0, 1))),
            token);
        return installed.Directory;
    }

    public Task<string> ImportArchiveAsync(string archivePath, CancellationToken token) =>
        Task.Run(async () =>
        {
            var name = Path.GetFileName(archivePath);
            foreach (var suffix in new[] { ".tar.xz", ".tar.gz", ".zip" })
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^suffix.Length];
                    break;
                }

            var home = Path.Combine(AndroidJavaPaths.Runtimes(global::Android.App.Application.Context), "imported", name);
            await JavaRuntimeTar.ExtractAsync(archivePath, home, token);
            return home;
        }, token);
}
