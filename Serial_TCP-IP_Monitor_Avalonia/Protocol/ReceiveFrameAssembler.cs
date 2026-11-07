namespace Serial_TCP_IP_Monitor_Avalonia.Protocol;

public enum ReceiveDelimiterMode
{
    None,
    Auto,
    CR,
    LF,
    CRLF
}

public sealed record ReceivedFrame(byte[] Payload, string DelimiterLabel);

public sealed class ReceiveFrameAssembler
{
    private readonly int _maximumBufferBytes;
    private readonly List<byte> _buffer = [];

    public ReceiveFrameAssembler(int maximumBufferBytes = 64 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBufferBytes);
        _maximumBufferBytes = maximumBufferBytes;
    }

    public bool WasTrimmed { get; private set; }
    public int BufferedByteCount => _buffer.Count;

    public IReadOnlyList<ReceivedFrame> Append(byte[]? data, ReceiveDelimiterMode mode)
    {
        WasTrimmed = false;
        if (data is not { Length: > 0 })
        {
            return [];
        }

        if (mode == ReceiveDelimiterMode.None)
        {
            return [new ReceivedFrame((byte[])data.Clone(), string.Empty)];
        }

        _buffer.AddRange(data);
        var frames = ExtractFrames(mode);
        TrimIfNeeded();
        return frames;
    }

    public ReceivedFrame? FlushIncomplete(ReceiveDelimiterMode mode = ReceiveDelimiterMode.None)
    {
        if (_buffer.Count == 0)
        {
            return null;
        }

        if (mode == ReceiveDelimiterMode.Auto && _buffer[^1] == 0x0D)
        {
            var autoPayload = _buffer.GetRange(0, _buffer.Count - 1).ToArray();
            _buffer.Clear();
            return new ReceivedFrame(autoPayload, "[CR]");
        }

        var payload = _buffer.ToArray();
        _buffer.Clear();
        return new ReceivedFrame(payload, string.Empty);
    }

    public void Clear()
    {
        _buffer.Clear();
        WasTrimmed = false;
    }

    private IReadOnlyList<ReceivedFrame> ExtractFrames(ReceiveDelimiterMode mode)
    {
        var frames = new List<ReceivedFrame>();
        var consumed = 0;

        while (TryFindDelimiter(consumed, mode, out var index, out var length, out var label))
        {
            frames.Add(new ReceivedFrame(_buffer.GetRange(consumed, index - consumed).ToArray(), label));
            consumed = index + length;
        }

        if (consumed > 0)
        {
            _buffer.RemoveRange(0, consumed);
        }

        return frames;
    }

    private bool TryFindDelimiter(int start, ReceiveDelimiterMode mode, out int index, out int length, out string label)
    {
        index = -1;
        length = 0;
        label = string.Empty;

        for (var i = start; i < _buffer.Count; i++)
        {
            var current = _buffer[i];
            switch (mode)
            {
                case ReceiveDelimiterMode.CR when current == 0x0D:
                    return SetDelimiter(i, 1, "[CR]", out index, out length, out label);
                case ReceiveDelimiterMode.LF when current == 0x0A:
                    return SetDelimiter(i, 1, "[LF]", out index, out length, out label);
                case ReceiveDelimiterMode.CRLF when current == 0x0D && i + 1 < _buffer.Count && _buffer[i + 1] == 0x0A:
                    return SetDelimiter(i, 2, "[CRLF]", out index, out length, out label);
                case ReceiveDelimiterMode.Auto when current == 0x0D:
                    if (i + 1 >= _buffer.Count)
                    {
                        return false;
                    }

                    return _buffer[i + 1] == 0x0A
                        ? SetDelimiter(i, 2, "[CRLF]", out index, out length, out label)
                        : SetDelimiter(i, 1, "[CR]", out index, out length, out label);
                case ReceiveDelimiterMode.Auto when current == 0x0A:
                    return SetDelimiter(i, 1, "[LF]", out index, out length, out label);
            }
        }

        return false;
    }

    private static bool SetDelimiter(int foundIndex, int foundLength, string foundLabel, out int index, out int length, out string label)
    {
        index = foundIndex;
        length = foundLength;
        label = foundLabel;
        return true;
    }

    private void TrimIfNeeded()
    {
        if (_buffer.Count <= _maximumBufferBytes)
        {
            return;
        }

        _buffer.RemoveRange(0, _buffer.Count - _maximumBufferBytes);
        WasTrimmed = true;
    }
}
