namespace PlcComLib.WpfTester.Models;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Debug,
}

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; } = LogLevel.Info;
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
