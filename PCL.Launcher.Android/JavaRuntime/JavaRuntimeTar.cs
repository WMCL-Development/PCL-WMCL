using System.Buffers;
using System.Formats.Tar;
using SharpCompress.Compressors.Xz;

namespace PCL.Android;

internal static class JavaRuntimeTar
{
    // 发行包当前解压后约 160-180 MiB；上限留出余量，同时阻止压缩炸弹。
    private const long MaxExpandedBytes = 512L * 1024 * 1024;
    // tar 本身（文件数据 + 头 + 填充）约 174-188 MiB；单独限制解压后的 tar 流，
    // 避免恶意 PAX/填充让 TarReader 读取无限数据。
    private const long MaxTarBytes = 1024L * 1024 * 1024;
    private const int MaxEntries = 4096;
    private const int MaxPathLength = 4096;
    private const int CopyBufferSize = 256 * 1024;
    private const long ProgressInterval = 4L * 1024 * 1024;

    // HotSpot 库必须位于应用私有目录。发行包里的相对符号链接留到所有普通文件落盘后再创建。
    public static async Task ExtractAsync(string archive, string destination, CancellationToken cancellationToken,
        Action<long>? progress = null)
    {
        await using var input = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var xz = new XZStream(input);
        var bounded = new BoundedReadStream(xz, MaxTarBytes);
        await ExtractTarAsync(bounded, destination, cancellationToken, progress).ConfigureAwait(false);
    }

    // 独立出来便于用内存 tar 流做恶意归档回归；XZ 只是外层容器。
    internal static async Task ExtractTarAsync(Stream tarStream, string destination,
        CancellationToken cancellationToken, Action<long>? progress = null)
    {
        var root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        var entries = new HashSet<string>(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        var links = new List<(string Path, string Target, string ResolvedTarget)>();
        long bytes = 0, lastProgress = 0;
        var count = 0;

        await using var tar = new TarReader(tarStream, leaveOpen: true);
        while (await tar.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > MaxEntries)
                throw new InvalidDataException("JDK 安装包包含过多条目。");
            if (entry.Name.Length == 0 || entry.Name.Length > MaxPathLength)
                throw new InvalidDataException("JDK 安装包包含无效路径。");

            ValidateRelativePath(entry.Name);
            if (HasParentSegment(entry.Name))
                throw new InvalidDataException($"JDK 安装包含有无效路径：{entry.Name}");
            var path = ResolveInside(root, root, entry.Name);

            if (entry.EntryType == TarEntryType.Directory)
            {
                // 发行包以 "./" 作为根目录；重复目录条目是合法的，幂等跳过即可。
                if (path == root)
                    continue;
                if (!entries.Add(path))
                {
                    if (directories.Contains(path))
                        continue;
                    throw new InvalidDataException($"JDK 安装包含有重复路径：{entry.Name}");
                }

                Directory.CreateDirectory(path);
                directories.Add(path);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, OwnerDirectoryMode);
                continue;
            }

            if (!entries.Add(path))
                throw new InvalidDataException($"JDK 安装包含有重复路径：{entry.Name}");

            var parent = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(parent);
            switch (entry.EntryType)
            {
                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                case TarEntryType.ContiguousFile:
                    if (entry.Length < 0 || entry.Length > MaxExpandedBytes - bytes)
                        throw new InvalidDataException("JDK 安装包的解压大小超过限制。");
                    bytes += entry.Length;
                    await WriteFileAsync(path, entry, cancellationToken).ConfigureAwait(false);
                    files.Add(path);
                    if (progress is not null && bytes - lastProgress >= ProgressInterval)
                    {
                        lastProgress = bytes;
                        progress(bytes);
                    }
                    break;
                case TarEntryType.SymbolicLink:
                    // 相对链接允许 ../（发行包里的 server/libjsig.so 就是这样），
                    // 但最终目标必须留在安装目录内，并且指向包内普通文件。
                    ValidateRelativePath(entry.LinkName, "链接目标");
                    links.Add((path, entry.LinkName, ResolveInside(root, parent, entry.LinkName)));
                    break;
                default:
                    throw new InvalidDataException($"JDK 安装包包含不支持的条目类型：{entry.EntryType}");
            }
        }

        foreach (var (path, target, resolvedTarget) in links)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!files.Contains(resolvedTarget))
                throw new InvalidDataException($"JDK 安装包中的链接未指向包内普通文件：{target}");
            File.CreateSymbolicLink(path, target);
        }

        progress?.Invoke(bytes);
    }

    private static async Task WriteFileAsync(string path, TarEntry entry, CancellationToken cancellationToken)
    {
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            long written = 0;
            if (entry.DataStream is { } data)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
                try
                {
                    int read;
                    while ((read = await data.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken)
                               .ConfigureAwait(false)) != 0)
                    {
                        written += read;
                        // 不只相信 tar 头里的 Length，实际拷贝也必须有界。
                        if (written > entry.Length || written > MaxExpandedBytes)
                            throw new InvalidDataException("JDK 安装包的文件内容超过声明大小。");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            if (written != entry.Length)
                throw new InvalidDataException("JDK 安装包的文件内容与声明大小不一致。");
        }

        if (!OperatingSystem.IsWindows())
        {
            // 运行库只需要所有者读取；保留发行包中的可执行位（bin/java、jexec 等）。
            File.SetUnixFileMode(path, UnixFileMode.UserRead | (entry.Mode & UnixFileMode.UserExecute));
        }
    }

    private static void ValidateRelativePath(string path, string kind = "路径")
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength || path.StartsWith('/') ||
            path.IndexOfAny(['\\', ':', '\0']) >= 0)
            throw new InvalidDataException($"JDK 安装包含有无效{kind}：{path}");
    }

    private static bool HasParentSegment(string path) =>
        path == ".." || path.StartsWith("../", StringComparison.Ordinal) ||
        path.EndsWith("/..", StringComparison.Ordinal) || path.Contains("/../", StringComparison.Ordinal);

    private static string ResolveInside(string root, string parent, string relativePath)
    {
        string path;
        try
        {
            path = Path.GetFullPath(Path.Combine(parent, relativePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException($"JDK 安装包含有无效路径：{relativePath}", exception);
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPrefix = Path.TrimEndingDirectorySeparator(root);
        var prefix = rootPrefix.EndsWith(Path.DirectorySeparatorChar)
            ? rootPrefix
            : rootPrefix + Path.DirectorySeparatorChar;
        if (!path.Equals(root, comparison) && !path.StartsWith(prefix, comparison))
            throw new InvalidDataException($"JDK 安装包路径越过安装目录：{relativePath}");
        return Path.TrimEndingDirectorySeparator(path);
    }

    private static UnixFileMode OwnerDirectoryMode =>
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    // 限制解压后的 tar 流总量；超过上限时按截断处理，TarReader 会报错或后续布局校验失败。
    private sealed class BoundedReadStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => limit;

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var allowed = Allowed(buffer.Length);
            if (allowed == 0)
                return 0;
            var read = inner.Read(buffer[..allowed]);
            _read += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var allowed = Allowed(buffer.Length);
            if (allowed == 0)
                return 0;
            var read = await inner.ReadAsync(buffer[..allowed], cancellationToken).ConfigureAwait(false);
            _read += read;
            return read;
        }

        private int Allowed(int requested)
        {
            if (requested <= 0)
                return 0;
            var remaining = limit - _read;
            return remaining <= 0 ? 0 : (int)Math.Min(requested, remaining);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
