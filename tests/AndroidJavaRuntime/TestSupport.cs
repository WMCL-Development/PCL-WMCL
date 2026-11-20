using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using PCL.Android;

namespace PCL.Android.Tests;

internal sealed class ScriptedHandler(Func<CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Requests;

    public static ScriptedHandler ForBytes(byte[] data, long? contentLength = null) =>
        new(_ => BytesResponse(data, contentLength));

    public static HttpResponseMessage BytesResponse(byte[] data, long? contentLength = null)
    {
        var content = new StreamContent(new MemoryStream(data));
        content.Headers.ContentLength = contentLength ?? data.Length;
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-xz");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    public static HttpResponseMessage StreamResponse(Stream stream, long contentLength)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentLength = contentLength;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Requests);
        var response = respond(cancellationToken);
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }
}

internal sealed class FailingStream(byte[] data, int failAfter) : Stream
{
    private readonly MemoryStream _inner = new(data);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => data.Length;

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_inner.Position >= failAfter)
            throw new IOException("simulated transfer failure");
        var allowed = Math.Min(buffer.Length, failAfter - (int)_inner.Position);
        return _inner.Read(buffer[..allowed]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<int>(cancellationToken);
        if (_inner.Position >= failAfter)
            throw new IOException("simulated transfer failure");
        var allowed = Math.Min(buffer.Length, failAfter - (int)_inner.Position);
        return ValueTask.FromResult(_inner.Read(buffer.Span[..allowed]));
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class ProgressSink(Action<JavaInstallProgress>? onReport = null) : IProgress<JavaInstallProgress>
{
    public List<JavaInstallProgress> Reports { get; } = [];

    public void Report(JavaInstallProgress value)
    {
        Reports.Add(value);
        onReport?.Invoke(value);
    }
}

internal sealed class TempRoot : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "wmcl-jrt-tests-" + Guid.NewGuid().ToString("N"));

    public TempRoot() => Directory.CreateDirectory(Path);

    public void Dispose() => TryDelete(Path);

    public static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"cleanup warning: {exception.Message}");
        }
    }
}

internal static class Fixtures
{
    public static string MinimalArchivePath =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "minimal-runtime.tar.xz");

    public static byte[] MinimalArchive()
    {
        var path = MinimalArchivePath;
        if (!File.Exists(path))
            throw new FileNotFoundException("minimal runtime fixture missing", path);
        return File.ReadAllBytes(path);
    }

    public static JavaRuntimePackage Package(byte[] archive) => new(
        17, "17.0.0", archive.LongLength,
        Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant());

    public static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
