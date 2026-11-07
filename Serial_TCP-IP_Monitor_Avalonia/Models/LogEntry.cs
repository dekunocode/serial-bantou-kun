namespace Serial_TCP_IP_Monitor_Avalonia.Models;

public enum LogCategory
{
    Receive,
    Transmit,
    System,
    Error
}

public sealed record LogEntry(DateTime Timestamp, LogCategory Category, string Message)
{
    public string CategoryLabel => Category switch
    {
        LogCategory.Receive => "RX",
        LogCategory.Transmit => "TX",
        LogCategory.Error => "ERROR",
        _ => "SYSTEM"
    };

    public string TimestampText => Timestamp.ToString("HH:mm:ss.fff");

    public string ToDisplayText() => $"[{TimestampText}] [{CategoryLabel}] {Message}";
}
