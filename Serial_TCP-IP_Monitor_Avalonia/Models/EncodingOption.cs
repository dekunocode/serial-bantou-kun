using System.Text;

namespace Serial_TCP_IP_Monitor_Avalonia.Models;

public sealed record EncodingOption(string DisplayName, Encoding Encoding)
{
    public override string ToString() => DisplayName;
}
