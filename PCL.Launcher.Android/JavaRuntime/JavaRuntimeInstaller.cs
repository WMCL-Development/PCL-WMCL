using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PCL.Android;

internal enum JavaInstallStage { Downloading, Verifying, Extracting }

internal readonly record struct JavaInstallProgress(JavaInstallStage Stage, long Bytes = 0);

internal sealed class JavaRuntimeInstaller
{
    private const string InstallMarker = ".wmcl-sha256";
    private const string StagingPrefix = ".install-";
    private const string RetiredPrefix = ".retired-";
    private const int DownloadBufferSize = 128 * 1024;
    private const long ProgressInterval = 256 * 1024;

    // 每个运行时根目录一个安装锁：同根目录串行（含不同版本），不同根目录可并行。
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InstallGates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string _root;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _gate;

    public JavaRuntimeInstaller(string root, HttpClient client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _gate = InstallGates.GetOrAdd(_root, static _ => new SemaphoreSlim(1, 1));
    }

    public InstalledJavaRuntime? FindInstalled(JavaRuntimePackage package) => FindInstalled(_root, package);

    // UI 进程和独立 JVM 进程共用同一份缓存校验逻辑，worker 不需要构造 HttpClient。
    public static InstalledJavaRuntime? FindInstalled(string root, JavaRuntimePackage package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(package);
        var runtime = new InstalledJavaRuntime(Path.Combine(Path.GetFullPath(root), package.Version), package);
        try
        {
            var marker = Path.Combine(runtime.Directory, InstallMarker);
            var markerInfo = new FileInfo(marker);
            if (!markerInfo.Exists || markerInfo.Length != package.Sha256.Length)
                return null;
            if (!string.Equals(File.ReadAllText(marker), package.Sha256, StringComparison.Ordinal))
                return null;
            return HasRuntimeLayout(runtime) ? runtime : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<InstalledJavaRuntime> EnsureInstalledAsync(
        JavaRuntimePackage package, IProgress<JavaInstallProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        // 已安装时完全不需要拿安装锁；这是启动路径上的热路径。
        if (FindInstalled(package) is { } cached)
            return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_root);
            // 上一次崩溃可能留下 100+ MiB 的 .retired-*/.install-*；放到线程池删，
            // 避免 GetAsync 还没真正异步时在 UI 线程上递归删除。
            await Task.Run(CleanupStaleEntries, cancellationToken).ConfigureAwait(false);
            if (FindInstalled(package) is { } installed)
                return installed;

            var staging = Path.Combine(_root, StagingPrefix + Guid.NewGuid().ToString("N"));
            var archive = Path.Combine(staging, "runtime.tar.xz");
            var payload = Path.Combine(staging, "jdk");
            try
            {
                Directory.CreateDirectory(payload);
                await DownloadAsync(package, archive, progress, cancellationToken).ConfigureAwait(false);

                progress?.Report(new(JavaInstallStage.Extracting));
                var progressSink = progress;
                Action<long>? extractProgress = progressSink is null
                    ? null
                    : bytes => progressSink.Report(new(JavaInstallStage.Extracting, bytes));
                await JavaRuntimeTar.ExtractAsync(archive, payload, cancellationToken, extractProgress)
                    .ConfigureAwait(false);

                if (!HasRuntimeLayout(new InstalledJavaRuntime(payload, package)))
                    throw new InvalidDataException("JDK 安装包的版本或运行时目录结构不正确。");

                await File.WriteAllTextAsync(Path.Combine(payload, InstallMarker), package.Sha256, Encoding.ASCII,
                    cancellationToken).ConfigureAwait(false);

                // 发布前最后一次取消检查；一旦开始重命名就让它完成，避免留下半发布状态。
                cancellationToken.ThrowIfCancellationRequested();
                Publish(payload, Path.Combine(_root, package.Version));
                return FindInstalled(package) ?? throw new InvalidDataException("JDK 安装完成后的完整性校验失败。");
            }
            finally
            {
                TryDelete(staging);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DownloadAsync(JavaRuntimePackage package, string archive,
        IProgress<JavaInstallProgress>? progress, CancellationToken cancellationToken)
    {
        using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        downloadTimeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = downloadTimeout.Token;
        try
        {
            progress?.Report(new(JavaInstallStage.Downloading));
            using var response = await _client.GetAsync(package.ArchiveUri, HttpCompletionOption.ResponseHeadersRead,
                token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != package.ArchiveSize)
                throw new InvalidDataException("服务器返回的 JDK 安装包大小与发布清单不符。");

            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: DownloadBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(DownloadBufferSize);
            try
            {
                long total = 0, lastReport = 0;
                int count;
                while ((count = await input.ReadAsync(buffer.AsMemory(0, DownloadBufferSize), token)
                           .ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > package.ArchiveSize)
                        throw new InvalidDataException("JDK 安装包超过发布清单指定的大小。");
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, count);
                    if (total - lastReport >= ProgressInterval || total == package.ArchiveSize)
                    {
                        lastReport = total;
                        progress?.Report(new(JavaInstallStage.Downloading, total));
                    }
                }

                progress?.Report(new(JavaInstallStage.Verifying));
                if (total != package.ArchiveSize ||
                    !Convert.ToHexString(hash.GetHashAndReset())
                        .Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("JDK 安装包的大小或 SHA-256 校验失败。");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("下载 JDK 超时，请重试。", exception);
        }
    }

    // 先改名为 .retired-*，再把完整 payload 改名到目标位置；重命名在同一文件系统内是原子的。
    // 若中途崩溃，下一次安装会清理 .retired-* 和 .install-*，不会误用半成品。
    private void Publish(string payload, string destination)
    {
        string? retired = null;
        if (PathExists(destination))
        {
            retired = Path.Combine(_root, RetiredPrefix + Guid.NewGuid().ToString("N"));
            MovePath(destination, retired);
        }

        try
        {
            Directory.Move(payload, destination);
        }
        catch
        {
            if (retired is not null && !PathExists(destination))
                TryMove(retired, destination);
            throw;
        }

        if (retired is not null)
            TryDelete(retired);
    }

    private static void MovePath(string source, string destination)
    {
        // 普通目录用 Directory.Move；文件、符号链接（含悬空链接）用 File.Move，
        // 避免把链接指向的目录整体搬走或误删外部内容。
        if (Directory.Exists(source) && new DirectoryInfo(source).LinkTarget is null)
            Directory.Move(source, destination);
        else
            File.Move(source, destination);
    }

    private static bool PathExists(string path)
    {
        try
        {
            return Directory.Exists(path) || File.Exists(path) || new FileInfo(path).LinkTarget is not null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryMove(string source, string destination)
    {
        try
        {
            MovePath(source, destination);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void CleanupStaleEntries()
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(_root))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith(StagingPrefix, StringComparison.Ordinal) ||
                name.StartsWith(RetiredPrefix, StringComparison.Ordinal))
                TryDelete(path);
        }
    }

    private static bool HasRuntimeLayout(InstalledJavaRuntime runtime)
    {
        try
        {
            if (!Directory.Exists(runtime.Directory) || !Directory.Exists(runtime.JavaHome))
                return false;

            var release = Path.Combine(runtime.Directory, "release");
            var runtimeImage = runtime.Package.Major == 8
                ? Path.Combine(runtime.JavaHome, "lib", "rt.jar")
                : Path.Combine(runtime.LibraryDirectory, "modules");
            var java = Path.Combine(runtime.JavaHome, "bin", "java");

            if (!IsNonEmptyFile(release) || new FileInfo(release).Length > 64 * 1024 ||
                !IsNonEmptyFile(runtimeImage) || !IsNonEmptyFile(java) ||
                !IsNonEmptyFile(Path.Combine(runtime.LibraryDirectory, "libjava.so")) ||
                !IsNonEmptyFile(runtime.JvmLibrary))
                return false;

            var properties = File.ReadAllLines(release);
            return properties.Contains($"JAVA_VERSION=\"{runtime.Package.Version}\"") &&
                   properties.Contains("OS_ARCH=\"aarch64\"");
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsNonEmptyFile(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null)
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path) || new FileInfo(path).LinkTarget is not null)
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
