using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;

namespace PCL.Core.Link.Terracotta;

/// <summary>
///     陶瓦房主的本地端口桥接：编号里的端口由网络名推导，无法指定，
///     所以在本机监听该端口并转发到真正的局域网端口。
/// </summary>
public sealed class TerracottaTunnel : IDisposable
{
    private readonly TcpListener _listener;
    private readonly int _targetPort;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public TerracottaTunnel(int listenPort, int targetPort)
    {
        _targetPort = targetPort;
        _listener = new TcpListener(IPAddress.Any, listenPort);
        ListenPort = listenPort;
    }

    public int ListenPort { get; }

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
        LogWrapper.Info("Terracotta", $"端口桥接已启动：{ListenPort} -> 127.0.0.1:{_targetPort}");
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient incoming;
            try
            {
                incoming = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                LogWrapper.Warn("Terracotta", "接受连接失败：" + exception.Message);
                continue;
            }

            _ = BridgeAsync(incoming, token);
        }
    }

    private async Task BridgeAsync(TcpClient incoming, CancellationToken token)
    {
        using var source = incoming;
        using var target = new TcpClient();
        try
        {
            await target.ConnectAsync(IPAddress.Loopback, _targetPort, token).ConfigureAwait(false);
            var sourceStream = source.GetStream();
            var targetStream = target.GetStream();
            var up = sourceStream.CopyToAsync(targetStream, token);
            var down = targetStream.CopyToAsync(sourceStream, token);
            await Task.WhenAny(up, down).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogWrapper.Debug("Terracotta", "转发连接结束：" + exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
            // 忽略关闭时的异常。
        }

        _cts.Dispose();
    }
}
