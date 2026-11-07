using System.IO.Ports;

namespace Serial_TCP_IP_Monitor_Avalonia.Services;

public sealed class SerialPortService : IDisposable
{
    private readonly SerialPort _serialPort = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    public SerialPortService()
    {
        _serialPort.DataReceived += OnDataReceived;
        _serialPort.ErrorReceived += OnErrorReceived;
    }

    public event Action<byte[]>? DataReceived;
    public event Action<string>? ErrorOccurred;
    public event Action<string>? WarningOccurred;

    public bool IsOpen => !_disposed && _serialPort.IsOpen;

    public void Open(
        string portName,
        int baudRate,
        Parity parity,
        int dataBits,
        StopBits stopBits,
        Handshake handshake,
        bool dtrEnable,
        bool rtsEnable)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOpen)
        {
            return;
        }

        _serialPort.PortName = portName;
        _serialPort.BaudRate = baudRate;
        _serialPort.Parity = parity;
        _serialPort.DataBits = dataBits;
        _serialPort.StopBits = stopBits;
        _serialPort.Handshake = handshake;
        _serialPort.DtrEnable = dtrEnable;
        _serialPort.RtsEnable = UsesRtsFlowControl(handshake) || rtsEnable;
        _serialPort.Open();
    }

    public void Close()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_serialPort.IsOpen)
        {
            _serialPort.Close();
        }
    }

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsOpen)
        {
            throw new InvalidOperationException("シリアルポートが開かれていません。");
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _serialPort.BaseStream.WriteAsync(data, cancellationToken);
            await _serialPort.BaseStream.FlushAsync(cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var available = _serialPort.BytesToRead;
            if (available <= 0)
            {
                return;
            }

            var buffer = new byte[available];
            var count = _serialPort.Read(buffer, 0, buffer.Length);
            if (count <= 0)
            {
                return;
            }

            if (count != buffer.Length)
            {
                Array.Resize(ref buffer, count);
            }

            DataReceived?.Invoke(buffer);
        }
        catch (Exception ex) when (!_disposed)
        {
            ErrorOccurred?.Invoke($"シリアル受信エラー: {ex.Message}");
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        WarningOccurred?.Invoke($"シリアル通信警告: {e.EventType}");
    }

    private static bool UsesRtsFlowControl(Handshake handshake) =>
        handshake is Handshake.RequestToSend or Handshake.RequestToSendXOnXOff;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _serialPort.DataReceived -= OnDataReceived;
        _serialPort.ErrorReceived -= OnErrorReceived;
        try
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }
        finally
        {
            _serialPort.Dispose();
            _sendLock.Dispose();
        }
    }
}
