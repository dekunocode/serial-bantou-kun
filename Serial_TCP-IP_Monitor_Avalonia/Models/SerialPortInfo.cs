namespace Serial_TCP_IP_Monitor_Avalonia.Models;

public sealed record SerialPortInfo(string PortName, string DisplayName)
{
    public override string ToString() => DisplayName;
}
