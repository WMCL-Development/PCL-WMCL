using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;

namespace PCL.Core.IO.Net.Http;

/// <summary>
///     托管 HTTP 服务收到的请求。替代 <see cref="HttpListenerRequest" />，
///     避免 Windows 的 Http.Sys URL ACL 限制，让四个平台行为一致。
/// </summary>
public sealed class HttpServerRequest
{
    public required string HttpMethod { get; init; }

    public required Uri Url { get; init; }

    public required Version ProtocolVersion { get; init; }

    public required NameValueCollection Headers { get; init; }

    public required Stream InputStream { get; init; }

    public Encoding ContentEncoding { get; init; } = Encoding.UTF8;

    public long ContentLength64 { get; init; }

    public bool HasEntityBody => ContentLength64 > 0;

    /// <summary>请求是否来自本机回环地址。</summary>
    public bool IsLocal { get; init; }

    public string? ContentType => Headers["Content-Type"];
}
