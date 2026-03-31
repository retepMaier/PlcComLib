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

    /// <summary>
    /// Fired when a received payload cannot be matched to any registered telegram definition.
    /// Use this to log, inspect, or handle unrecognised messages.
    /// </summary>
    event EventHandler<UnknownTelegramEventArgs> UnknownTelegramReceived;

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

/// <summary>
/// Event args raised when a received payload cannot be matched to any registered telegram definition.
/// </summary>
public sealed class UnknownTelegramEventArgs : EventArgs
{
    public UnknownTelegramEventArgs(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Payload = payload;
    }

    /// <summary>
    /// The raw wire bytes of the unrecognised message (after any framing has been stripped).
    /// </summary>
    public byte[] Payload { get; }

    /// <summary>
    /// The first two bytes of <see cref="Payload"/> interpreted as a big-endian <c>ushort</c>,
    /// or <c>0</c> if the payload is shorter than 2 bytes.
    /// </summary>
    public ushort CandidateTelegramId =>
        Payload.Length >= 2
            ? (ushort)((Payload[0] << 8) | Payload[1])
            : (ushort)0;
}
