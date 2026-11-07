using System.Text;
using System.Net;
using System.Net.Sockets;
using Serial_TCP_IP_Monitor_Avalonia.Protocol;
using Serial_TCP_IP_Monitor_Avalonia.Services;

var tests = new (string Name, Func<Task> Execute)[]
{
    ("CRLF分割受信", () => RunSync(TestFragmentedCrLf)),
    ("Auto末尾CR確定", () => RunSync(TestAutoTrailingCr)),
    ("明示区切り保持", () => RunSync(TestExplicitDelimiterRetention)),
    ("制御コード生バイト変換", () => RunSync(TestMixedControlCodeBytes)),
    ("Hex入力変換", () => RunSync(TestHexInput)),
    ("受信バッファ上限", () => RunSync(TestBufferLimit)),
    ("TCPループバック送受信", TestTcpRoundTripAsync),
    ("LF/CRLF終端の自動判定", () => RunSync(TestAutoDelimiterLfAndCrLf)),
    ("設定シリアライズ検証", () => RunSync(TestSettingsSerialization))
};

var failures = 0;
foreach (var (name, execute) in tests)
{
    try
    {
        await execute();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {name} - {ex.Message}");
    }
}

return failures == 0 ? 0 : 1;

static Task RunSync(Action action)
{
    action();
    return Task.CompletedTask;
}

static void TestFragmentedCrLf()
{
    var assembler = new ReceiveFrameAssembler();
    AssertEqual(0, assembler.Append(Encoding.ASCII.GetBytes("ABC\r"), ReceiveDelimiterMode.CRLF).Count);
    var frames = assembler.Append([0x0A], ReceiveDelimiterMode.CRLF);
    AssertEqual(1, frames.Count);
    AssertSequence(Encoding.ASCII.GetBytes("ABC"), frames[0].Payload);
    AssertEqual("[CRLF]", frames[0].DelimiterLabel);
}

static void TestAutoTrailingCr()
{
    var assembler = new ReceiveFrameAssembler();
    AssertEqual(0, assembler.Append(Encoding.ASCII.GetBytes("ABC\r"), ReceiveDelimiterMode.Auto).Count);
    var frame = assembler.FlushIncomplete(ReceiveDelimiterMode.Auto) ?? throw new InvalidOperationException("frame is null");
    AssertSequence(Encoding.ASCII.GetBytes("ABC"), frame.Payload);
    AssertEqual("[CR]", frame.DelimiterLabel);
}

static void TestExplicitDelimiterRetention()
{
    var assembler = new ReceiveFrameAssembler();
    AssertEqual(0, assembler.Append(Encoding.ASCII.GetBytes("PARTIAL"), ReceiveDelimiterMode.LF).Count);
    AssertEqual(7, assembler.BufferedByteCount);
    var frames = assembler.Append([0x0A], ReceiveDelimiterMode.LF);
    AssertSequence(Encoding.ASCII.GetBytes("PARTIAL"), frames.Single().Payload);
}

static void TestMixedControlCodeBytes()
{
    AssertSequence([0x02, 0x41, 0xFF, 0x03], ControlCodeCodec.ParseMixedText("[STX]A[0xFF][ETX]", Encoding.UTF8));
    AssertSequence(Encoding.ASCII.GetBytes("A[UNKNOWN]B"), ControlCodeCodec.ParseMixedText("A[UNKNOWN]B", Encoding.ASCII));
}

static void TestHexInput()
{
    AssertEqual(true, ControlCodeCodec.TryParseHexBytes("02, FF 0x03", out var bytes, out _));
    AssertSequence([0x02, 0xFF, 0x03], bytes);
    AssertEqual(false, ControlCodeCodec.TryParseHexBytes("0G", out _, out var error));
    AssertEqual(false, string.IsNullOrWhiteSpace(error));
}

static void TestBufferLimit()
{
    var assembler = new ReceiveFrameAssembler(4);
    assembler.Append([1, 2, 3, 4, 5], ReceiveDelimiterMode.CRLF);
    AssertEqual(true, assembler.WasTrimmed);
    AssertEqual(4, assembler.BufferedByteCount);
}

static async Task TestTcpRoundTripAsync()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var acceptTask = listener.AcceptTcpClientAsync();

    using var service = new TcpCommunicationService();
    var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    service.DataReceived += data => received.TrySetResult(data);

    AssertEqual(true, await service.ConnectAsync(IPAddress.Loopback.ToString(), port));
    using var serverClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(3));
    await using var serverStream = serverClient.GetStream();

    AssertEqual(true, await service.SendAsync([0x10, 0x20, 0x30]));
    var serverBuffer = new byte[3];
    await serverStream.ReadExactlyAsync(serverBuffer).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    AssertSequence([0x10, 0x20, 0x30], serverBuffer);

    await serverStream.WriteAsync(new byte[] { 0xAA, 0x55 });
    AssertSequence([0xAA, 0x55], await received.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    service.Disconnect();
}

static void AssertSequence(byte[] expected, byte[] actual)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException($"expected={BitConverter.ToString(expected)}, actual={BitConverter.ToString(actual)}");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected={expected}, actual={actual}");
    }
}

static void TestAutoDelimiterLfAndCrLf()
{
    var assembler = new ReceiveFrameAssembler();
    var framesLf = assembler.Append(Encoding.ASCII.GetBytes("TEMP=23.5\n"), ReceiveDelimiterMode.Auto);
    AssertEqual(1, framesLf.Count);
    AssertEqual("TEMP=23.5", Encoding.ASCII.GetString(framesLf[0].Payload));
    AssertEqual("[LF]", framesLf[0].DelimiterLabel);

    var framesCrLf = assembler.Append(Encoding.ASCII.GetBytes("+1.234567E-01\r\n"), ReceiveDelimiterMode.Auto);
    AssertEqual(1, framesCrLf.Count);
    AssertEqual("+1.234567E-01", Encoding.ASCII.GetString(framesCrLf[0].Payload));
    AssertEqual("[CRLF]", framesCrLf[0].DelimiterLabel);
}

static void TestSettingsSerialization()
{
    var settings = new Serial_TCP_IP_Monitor_Avalonia.Models.AppSettings
    {
        Mode = "Tcp",
        TcpHost = "192.168.0.10",
        TcpPort = 5025
    };

    var json = System.Text.Json.JsonSerializer.Serialize(settings, Serial_TCP_IP_Monitor_Avalonia.Serialization.AppJsonContext.Default.AppSettings);
    var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, Serial_TCP_IP_Monitor_Avalonia.Serialization.AppJsonContext.Default.AppSettings)
        ?? throw new InvalidOperationException("deserialized is null");
    AssertEqual("Tcp", deserialized.Mode);
    AssertEqual("192.168.0.10", deserialized.TcpHost);
    AssertEqual(5025, deserialized.TcpPort);

}
