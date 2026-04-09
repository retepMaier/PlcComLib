namespace PlcComLib.Core.Events;

/// <summary>Event args for a connection state change.</summary>
public sealed class ConnectionStateChangedEventArgs(bool isConnected, string? reason = null) : EventArgs
{
    public bool IsConnected { get; } = isConnected;
    public string? Reason { get; } = reason;
}
