using System.IO.Ports;

namespace Serial_TCP_IP_Monitor_Avalonia.Models;

public sealed class AppSettings
{
    public string SerialParity { get; set; } = Parity.None.ToString();
    public int SerialDataBits { get; set; } = 8;
    public string SerialStopBits { get; set; } = StopBits.One.ToString();
    public string SerialHandshake { get; set; } = Handshake.None.ToString();
    public bool SerialDtrEnable { get; set; } = true;
    public bool SerialRtsEnable { get; set; } = true;
    public string EncodingName { get; set; } = "UTF-8";
    public string RxDelimiter { get; set; } = "Auto";
    public string TxDelimiter { get; set; } = "CRLF";
    public bool ShowDelimiterTags { get; set; } = true;
    public string TcpHost { get; set; } = "127.0.0.1";
    public int TcpPort { get; set; } = 23;
    public string Mode { get; set; } = "Serial";
}
