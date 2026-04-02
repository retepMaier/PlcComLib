namespace PlcComLib.WpfTester.Models;

public enum MessageDirection
{
    Sent,
    Received,
}

public sealed class MessageEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public MessageDirection Direction { get; init; }
    public byte[] RawBytes { get; init; } = [];
    public string HexDisplay => string.Join(" ", RawBytes.Select(b => b.ToString("X2")));
    public Dictionary<string, string> Fields { get; init; } = [];
    public string? TelegramName { get; init; }
    public string RemoteAddress { get; init; } = string.Empty;
    public int RemotePort { get; init; }
    public string DirectionLabel => Direction == MessageDirection.Sent ? "↑ Sent" : "↓ Received";
}
