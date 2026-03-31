using PlcComLib.Telegrams;

namespace PlcComLib.Core;

/// <summary>
/// Represents a bidirectional PLC communication channel.
/// </summary>
public interface IPlcConnection : IAsyncDisposable
{
    /// <summary>Fired whenever a complete telegram has been received.</summary>
    event EventHandler<TelegramReceivedEventArgs> TelegramReceived;

    /// <summary>Fired when the connection state changes.</summary>
    event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

    /// <summary>Gets the current connection state.</summary>
    bool IsConnected { get; }

    /// <summary>Opens the connection (starts listening or connects to the remote endpoint).</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the connection gracefully.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a serialised telegram to the remote side.</summary>
    Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default);
}

/// <summary>Event args for a received telegram.</summary>
public sealed class TelegramReceivedEventArgs : EventArgs
{
    public TelegramReceivedEventArgs(Telegram telegram, byte[]? rawPayload = null)
    {
        Telegram   = telegram;
        RawPayload = rawPayload ?? [];
    }

    public Telegram Telegram   { get; }
    /// <summary>The raw wire bytes that produced this telegram (includes the MessageId header for typed telegrams).</summary>
    public byte[]   RawPayload { get; }
}

/// <summary>Event args for a connection state change.</summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionStateChangedEventArgs(bool isConnected, string? reason = null)
    {
        IsConnected = isConnected;
        Reason = reason;
    }

    public bool IsConnected { get; }
    public string? Reason { get; }
}
