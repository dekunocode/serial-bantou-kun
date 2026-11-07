using System.IO.Ports;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Serial_TCP_IP_Monitor_Avalonia.Models;

namespace Serial_TCP_IP_Monitor_Avalonia.Services;

public sealed class SerialPortDiscoveryService
{
    public async Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(CancellationToken cancellationToken = default)
    {
        var ports = await Task.Run(GetPorts, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return ports;
    }

    public IReadOnlyList<SerialPortInfo> GetPorts()
    {
        var displayNames = OperatingSystem.IsWindows() ? GetWindowsDisplayNames() : [];
        return SerialPort.GetPortNames()
            .Select(portName => new SerialPortInfo(
                portName,
                displayNames.TryGetValue(portName, out var displayName)
                    ? $"{portName} — {displayName}"
                    : portName))
            .OrderBy(port => PortSortKey(port.PortName))
            .ThenBy(port => port.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int PortSortKey(string portName) =>
        int.TryParse(Regex.Match(portName, @"\d+$").Value, out var number) ? number : int.MaxValue;

    [SupportedOSPlatform("windows")]
    private static Dictionary<string, string> GetWindowsDisplayNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                var caption = item["Caption"]?.ToString();
                if (string.IsNullOrWhiteSpace(caption))
                {
                    continue;
                }

                var match = Regex.Match(caption, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var portName = match.Groups[1].Value;
                    result[portName] = caption.Replace($"({portName})", string.Empty).Trim();
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
        }

        return result;
    }
}
