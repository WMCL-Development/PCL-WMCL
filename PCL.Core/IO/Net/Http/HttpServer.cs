using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;

namespace PCL.Core.IO.Net.Http;

/// <summary>
///     轻量 HTTP 服务器：基于 <see cref="TcpListener" /> 自行解析 HTTP/1.1，
///     不依赖 <see cref="HttpListener" />。这样 Windows（Http.Sys URL ACL）、
///     Linux、macOS 与 Android 上的行为完全一致，也不会出现主机名前缀不匹配的 404。
///     仅用于本机回环回调等小流量场景。
/// </summary>
public abstract class HttpServer : IDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 4 * 1024 * 1024;

    private readonly List<TcpListener> _listeners = [];
    private readonly Dictionary<(HttpMethod method, string path),
        Func<HttpServerRequest, Task<HttpRouteResponse>>> _handlers = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _initialized;

    public readonly ushort Port;
    public readonly string[] Host;

    protected HttpServer(IPAddress[] listenAddr, ushort port = 0)
    {
        ArgumentNullException.ThrowIfNull(listenAddr);

        if (listenAddr.Length == 0)
            listenAddr = [IPAddress.Loopback, IPAddress.IPv6Loopback];

        var first = CreateListener(listenAddr[0], port);
        Port = (ushort)((IPEndPoint)first.LocalEndpoint).Port;
        _listeners.Add(first);

        var hosts = new List<string> { listenAddr[0].ToString() };
        foreach (var address in listenAddr.Skip(1))
        {
            try
            {
                _listeners.Add(CreateListener(address, Port));
                hosts.Add(address.ToString());
            }
            catch (Exception ex)
            {
                LogWrapper.Warn(ex, "HttpServer", $"监听地址失败：{address}:{Port}");
            }
        }

        Host = hosts.ToArray();
    }

    private static TcpListener CreateListener(IPAddress address, ushort port)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        return listener;
    }

    /// <summary>初始化路由。子类应在此方法中调用 Register 方法注册路由。</summary>
    protected abstract void Init();

    /// <summary>注册一个路由处理器。</summary>
    protected void Register(
        HttpMethod method,
        string path,
        Func<HttpServerRequest, Task<HttpRouteResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[(method, path)] = handler;
    }

    /// <summary>启动 HTTP 服务器。</summary>
    public void Start()
    {
        if (!_initialized && _handlers.Count == 0)
        {
            Init();
            _initialized = true;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        foreach (var listener in _listeners)
            _ = Task.Run(() => _AcceptLoopAsync(listener, token), token);
    }

    private async Task _AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            catch (Exception ex)
            {
                LogWrapper.Debug(ex, "HttpServer", "接受连接失败");
                break;
            }

            _ = Task.Run(() => _ProcessClientAsync(client, token), token);
        }
    }

    private async Task _ProcessClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();

                var remote = client.Client.RemoteEndPoint as IPEndPoint;
                var isLocal = remote is null || IPAddress.IsLoopback(remote.Address);
                var request = await _ReadRequestAsync(stream, isLocal, token).ConfigureAwait(false);
                if (request is null) return;

                var response = await _DispatchAsync(request).ConfigureAwait(false);
                await _WriteResponseAsync(stream, response, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogWrapper.Debug(ex, "HttpServer", "处理请求失败");
            }
        }
    }

    private async Task<HttpServerRequest?> _ReadRequestAsync(
        Stream stream,
        bool isLocal,
        CancellationToken token)
    {
        var headerBuffer = new MemoryStream(MaxHeaderBytes);
        var oneByte = new byte[1];
        var headersDone = false;

        while (headerBuffer.Length < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(oneByte, token).ConfigureAwait(false);
            if (read == 0) return null;

            headerBuffer.WriteByte(oneByte[0]);
            var length = (int)headerBuffer.Length;
            var buffer = headerBuffer.GetBuffer();

            if (length >= 4
                && buffer[length - 4] == (byte)13 && buffer[length - 3] == (byte)10
                && buffer[length - 2] == (byte)13 && buffer[length - 1] == (byte)10)
            {
                headersDone = true;
                break;
            }

            if (length >= 2
                && buffer[length - 2] == (byte)10 && buffer[length - 1] == (byte)10)
            {
                headersDone = true;
                break;
            }
        }

        if (!headersDone) return null;

        var headerText = Encoding.ASCII.GetString(
            headerBuffer.GetBuffer(),
            0,
            (int)headerBuffer.Length);
        var lines = headerText.Replace("\r\n", "\n").Split((char)10);
        if (lines.Length == 0) return null;

        var requestLine = lines[0].Trim().Split(' ', 3);
        if (requestLine.Length < 2) return null;

        var method = requestLine[0];
        var target = requestLine[1];
        var version = requestLine.Length >= 3 && requestLine[2].StartsWith("HTTP/", StringComparison.Ordinal)
            ? Version.Parse(requestLine[2]["HTTP/".Length..])
            : new Version(1, 1);

        var headers = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        var hostHeader = headers["Host"] ?? $"127.0.0.1:{Port}";
        Uri url;
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = new Uri(target);
        }
        else
        {
            url = new Uri($"http://{hostHeader}{target}");
        }

        long contentLength = 0;
        if (long.TryParse(headers["Content-Length"], out var parsedLength) && parsedLength > 0)
            contentLength = Math.Min(parsedLength, MaxBodyBytes);

        Stream body = Stream.Null;
        if (contentLength > 0)
        {
            var bodyBuffer = new byte[(int)contentLength];
            var offset = 0;
            while (offset < bodyBuffer.Length)
            {
                var read = await stream
                    .ReadAsync(bodyBuffer.AsMemory(offset, bodyBuffer.Length - offset), token)
                    .ConfigureAwait(false);
                if (read == 0) break;
                offset += read;
            }

            body = new MemoryStream(bodyBuffer, 0, offset, false, true);
        }

        var encoding = Encoding.UTF8;
        if (headers["Content-Type"] is { } contentType
            && contentType.Contains("charset=", StringComparison.OrdinalIgnoreCase))
        {
            var index = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase) + "charset=".Length;
            var charset = contentType[index..].Trim().Trim('"').Split(';')[0].Trim();
            try { encoding = Encoding.GetEncoding(charset); }
            catch (Exception) { encoding = Encoding.UTF8; }
        }

        return new HttpServerRequest
        {
            HttpMethod = method,
            Url = url,
            ProtocolVersion = version,
            Headers = headers,
            InputStream = body,
            ContentEncoding = encoding,
            ContentLength64 = contentLength,
            IsLocal = isLocal
        };
    }

    private async Task<HttpRouteResponse> _DispatchAsync(HttpServerRequest request)
    {
        var path = request.Url.AbsolutePath;
        var method = new HttpMethod(request.HttpMethod);

        if (_handlers.TryGetValue((method, path), out var handler))
            return await handler(request).ConfigureAwait(false);

        if (_handlers.TryGetValue((method, "*"), out var wildcardHandler))
            return await wildcardHandler(request).ConfigureAwait(false);

        return HttpRouteResponse.NotFound;
    }

    private static async Task _WriteResponseAsync(
        Stream stream,
        HttpRouteResponse response,
        CancellationToken token)
    {
        var statusCode = (int)(response.StatusCode ?? HttpStatusCode.OK);
        var reason = _GetReasonPhrase(statusCode);

        byte[]? body = null;
        if (response.InputStream is { } input)
        {
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, token).ConfigureAwait(false);
            body = buffer.ToArray();
        }

        var builder = new StringBuilder();
        builder.Append($"HTTP/1.1 {statusCode} {reason}\r\n");
        builder.Append("Connection: close\r\n");
        if (response.RedirectLocation is { } location)
            builder.Append($"Location: {location}\r\n");
        if (response.ContentType is { } contentType)
            builder.Append($"Content-Type: {contentType}\r\n");
        if (body is not null)
            builder.Append($"Content-Length: {body.Length}\r\n");
        else
            builder.Append("Content-Length: 0\r\n");
        if (response.Cookies is { } cookies)
            foreach (Cookie cookie in cookies)
                builder.Append($"Set-Cookie: {cookie.Name}={cookie.Value}; Path={cookie.Path}\r\n");
        builder.Append("\r\n");

        var headerBytes = Encoding.UTF8.GetBytes(builder.ToString());
        await stream.WriteAsync(headerBytes, token).ConfigureAwait(false);
        if (body is { Length: > 0 })
            await stream.WriteAsync(body, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static string _GetReasonPhrase(int statusCode)
    {
        return statusCode switch
        {
            200 => "OK",
            204 => "No Content",
            301 => "Moved Permanently",
            302 => "Found",
            304 => "Not Modified",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            _ => "Unknown"
        };
    }

    /// <summary>停止 HTTP 服务器。</summary>
    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        foreach (var listener in _listeners)
        {
            try { listener.Stop(); }
            catch (Exception) { /* ignore */ }
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Stop();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }
}
