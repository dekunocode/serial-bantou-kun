using System.Net.Sockets;

namespace Serial_TCP_IP_Monitor_Avalonia.Services;

public sealed class TcpCommunicationService : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _connectionCts;
    private bool _disposed;

    public event Action<byte[]>? DataReceived;
    public event Action<string>? ErrorOccurred;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _client?.Connected == true && _stream is not null;
            }
        }
    }

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        {
            return false;
        }

        Disconnect();
        var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectTimeout);

        try
        {
            await client.ConnectAsync(host, port, timeoutCts.Token);
            var stream = client.GetStream();
            var connectionCts = new CancellationTokenSource();

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _client = client;
                _stream = stream;
                _connectionCts = connectionCts;
            }

            _ = ReceiveLoopAsync(client, stream, connectionCts);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            ErrorOccurred?.Invoke("TCP接続がタイムアウトしました。");
            return false;
        }
        catch (Exception ex)
        {
            client.Dispose();
            ErrorOccurred?.Invoke($"TCP接続エラー: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        NetworkStream? stream;
        CancellationToken connectionToken;
        lock (_sync)
        {
            stream = _stream;
            connectionToken = _connectionCts?.Token ?? CancellationToken.None;
        }

        if (stream is null)
        {
            return false;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionToken);
        var lockTaken = false;
        try
        {
            await _sendLock.WaitAsync(linkedCts.Token);
            lockTaken = true;
            await stream.WriteAsync(data, linkedCts.Token);
            await stream.FlushAsync(linkedCts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"TCP送信エラー: {ex.Message}");
            Disconnect();
            return false;
        }
        finally
        {
            if (lockTaken)
            {
                _sendLock.Release();
            }
        }
    }

    private async Task ReceiveLoopAsync(TcpClient client, NetworkStream stream, CancellationTokenSource connectionCts)
    {
        var buffer = new byte[4096];
        try
        {
            while (!connectionCts.IsCancellationRequested)
            {
                var count = await stream.ReadAsync(buffer, connectionCts.Token);
                if (count == 0)
                {
                    ErrorOccurred?.Invoke("TCP接続先から切断されました。");
                    break;
                }

                DataReceived?.Invoke(buffer[..count]);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!connectionCts.IsCancellationRequested)
        {
            ErrorOccurred?.Invoke($"TCP受信エラー: {ex.Message}");
        }
        finally
        {
            DisconnectIfCurrent(client, connectionCts);
        }
    }

    public void Disconnect()
    {
        TcpClient? client;
        NetworkStream? stream;
        CancellationTokenSource? cts;
        lock (_sync)
        {
            client = _client;
            stream = _stream;
            cts = _connectionCts;
            _client = null;
            _stream = null;
            _connectionCts = null;
        }

        try { cts?.Cancel(); } catch { }
        try { stream?.Dispose(); } catch { }
        try { client?.Dispose(); } catch { }
        cts?.Dispose();
    }

    private void DisconnectIfCurrent(TcpClient client, CancellationTokenSource cts)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(client, _client) || !ReferenceEquals(cts, _connectionCts))
            {
                return;
            }
        }

        Disconnect();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disconnect();
        _sendLock.Dispose();
    }
}
