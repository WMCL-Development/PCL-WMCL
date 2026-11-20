using System.Buffers;
using System.Security.Cryptography;

namespace PCL;

/// <summary>Prepares official game files inside an application-controlled directory. It does not install native libraries or launch Java.</summary>
public sealed class AndroidVanillaDownloader(HttpClient client)
{
    private const int Workers = 6;

    // Serialize preparation to avoid competing scans and writes in the same application cache.
    private static readonly SemaphoreSlim PreparationGate = new(1, 1);

    /// <param name="root">App-private cache root; never a shared or user-modifiable Minecraft directory.</param>
    /// <param name="progress">Receives aggregate completed-file counts; the notification may run on a worker thread.</param>
    /// <param name="cancellationToken">Cancels preparation. Verified files are retained for the next run.</param>
    public async Task<AndroidVanilla1211> PrepareAsync(string root, IProgress<AndroidVanillaProgress>? progress,
        CancellationToken cancellationToken)
    {
        await PreparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plan = await GetManifestAsync(root, cancellationToken).ConfigureAwait(false);

            await PrepareBatchAsync(plan.BootstrapFiles, AndroidVanillaStep.Bootstrap, progress, cancellationToken)
                .ConfigureAwait(false);
            var assets = plan.ReadAssets(await File.ReadAllBytesAsync(plan.AssetIndex.Destination, cancellationToken).ConfigureAwait(false));
            await PrepareBatchAsync(assets, AndroidVanillaStep.Assets, progress, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(plan.GameDirectory);
            return plan;
        }
        finally { PreparationGate.Release(); }
    }

    internal async Task<AndroidVanilla1211> GetManifestAsync(string root, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        var path = Path.Combine(root, "versions", AndroidVanilla1211.Version, "1.21.1.json");
        if (File.Exists(path))
        {
            await using var cached = File.OpenRead(path);
            try { return AndroidVanilla1211.Read(await ReadManifestBytesAsync(cached, token).ConfigureAwait(false), root); }
            catch (InvalidDataException) { /* Recover the manifest from its fixed origin. */ }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var fetchToken = deadline.Token;
        try
        {
            using var response = await FetchAsync(AndroidVanilla1211.ManifestUri, fetchToken).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is <= 0 or > AndroidVanilla1211.MaxManifestBytes)
                throw new InvalidDataException("Unexpected Minecraft version manifest size.");
            await using var input = await response.Content.ReadAsStreamAsync(fetchToken).ConfigureAwait(false);
            var bytes = await ReadManifestBytesAsync(input, fetchToken).ConfigureAwait(false);
            var plan = AndroidVanilla1211.Read(bytes, root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var staging = path + ".part-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(staging, bytes, fetchToken).ConfigureAwait(false);
                fetchToken.ThrowIfCancellationRequested();
                File.Move(staging, path, overwrite: true);
            }
            finally { File.Delete(staging); }
            return plan;
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Minecraft version manifest download exceeded its deadline.", error);
        }
    }

    private static async Task<ReadOnlyMemory<byte>> ReadManifestBytesAsync(Stream stream, CancellationToken token)
    {
        var bytes = new byte[AndroidVanilla1211.MaxManifestBytes + 1];
        var length = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, token).ConfigureAwait(false);
        if (length > AndroidVanilla1211.MaxManifestBytes)
            throw new InvalidDataException("Minecraft version manifest exceeds the limit.");
        return bytes.AsMemory(0, length);
    }

    private async Task PrepareBatchAsync(IReadOnlyList<AndroidVanillaFile> files, AndroidVanillaStep step,
        IProgress<AndroidVanillaProgress>? progress, CancellationToken cancellationToken)
    {
        var completed = 0;
        var progressLock = new object();
        progress?.Report(new AndroidVanillaProgress(step, 0, files.Count));
        await Parallel.ForEachAsync(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = Workers,
            CancellationToken = cancellationToken
        }, async (file, token) =>
        {
            await EnsureFileAsync(file, token).ConfigureAwait(false);
            if (progress is not null)
            {
                lock (progressLock)
                    progress.Report(new AndroidVanillaProgress(step, ++completed, files.Count));
            }
        }).ConfigureAwait(false);
    }

    internal async Task EnsureFileAsync(AndroidVanillaFile file, CancellationToken token)
    {
        if (await IsVerifiedAsync(file, token).ConfigureAwait(false)) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var downloadToken = deadline.Token;
        Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
        var staging = file.Destination + ".part-" + Guid.NewGuid().ToString("N");
        try
        {
            using var response = await FetchAsync(file.Url, downloadToken).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length != file.Size)
                throw new InvalidDataException("Unexpected length for a Minecraft download.");
            await using (var target = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var input = await response.Content.ReadAsStreamAsync(downloadToken).ConfigureAwait(false))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    long written = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(), downloadToken).ConfigureAwait(false)) != 0)
                    {
                        written += read;
                        if (written > file.Size) throw new InvalidDataException("Minecraft download exceeds declared size.");
                        hash.AppendData(buffer.AsSpan(0, read));
                        await target.WriteAsync(buffer.AsMemory(0, read), downloadToken).ConfigureAwait(false);
                    }
                    if (written != file.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != file.Sha1)
                        throw new InvalidDataException("Minecraft download failed size or SHA-1 verification.");
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
            downloadToken.ThrowIfCancellationRequested();
            File.Move(staging, file.Destination, overwrite: true);
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Minecraft file download exceeded its deadline.", error);
        }
        finally { File.Delete(staging); }
    }

    private static async Task<bool> IsVerifiedAsync(AndroidVanillaFile file, CancellationToken token)
    {
        var cache = new FileInfo(file.Destination);
        if (!cache.Exists || cache.Length != file.Size) return false;
        await using var stream = new FileStream(file.Destination, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA1.HashDataAsync(stream, token).ConfigureAwait(false)) == file.Sha1;
    }

    private async Task<HttpResponseMessage> FetchAsync(Uri url, CancellationToken token)
    {
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode && response.RequestMessage?.RequestUri is { } uri && uri == url)
            return response;
        using (response)
        {
            response.EnsureSuccessStatusCode();
            throw new InvalidDataException("Unexpected redirect for a pinned Minecraft download.");
        }
    }
}

public enum AndroidVanillaStep { Bootstrap, Assets }
public readonly record struct AndroidVanillaProgress(AndroidVanillaStep Step, int Completed, int Total);
