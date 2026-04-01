using PlcComLib.DataTypes;
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

    /// <summary>
    /// Fired immediately after raw bytes (including any framing) are written to the network.
    /// Subscribe to capture the exact bytes sent over the wire for diagnostic purposes.
    /// </summary>
    event EventHandler<RawBytesEventArgs> RawBytesSent;

    /// <summary>
    /// Fired immediately after raw bytes are read from the network (before framing is parsed).
    /// Subscribe to capture the exact bytes received over the wire for diagnostic purposes.
    /// </summary>
    event EventHandler<RawBytesEventArgs> RawBytesReceived;

    /// <summary>Gets the current connection state.</summary>
    bool IsConnected { get; }

    /// <summary>Opens the connection (starts listening or connects to the remote endpoint).</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the connection gracefully.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a serialised telegram to the remote side.</summary>
    Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default);
}

/// <summary>Event args carrying raw bytes from the wire, used for diagnostic events.</summary>
public sealed class RawBytesEventArgs : EventArgs
{
    public RawBytesEventArgs(byte[] data, string remoteAddress = "", int port = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data          = data;
        RemoteAddress = remoteAddress;
        Port          = port;
    }

    /// <summary>The raw bytes transferred over the network.</summary>
    public byte[] Data { get; }

    /// <summary>The IP address of the remote endpoint (destination for sent bytes, source for received bytes).</summary>
    public string RemoteAddress { get; }

    /// <summary>The port of the remote endpoint (destination for sent bytes, source for received bytes).</summary>
    public int Port { get; }
}

/// <summary>Event args for a received telegram.</summary>
public sealed class TelegramReceivedEventArgs : EventArgs
{
    public TelegramReceivedEventArgs(Telegram telegram, byte[]? rawPayload = null, string remoteAddress = "", int port = 0)
    {
        Telegram      = telegram;
        RawPayload    = rawPayload ?? [];
        RemoteAddress = remoteAddress;
        Port          = port;
    }

    public Telegram Telegram { get; }

    /// <summary>The raw wire bytes that produced this telegram (includes the MessageId header for typed telegrams).</summary>
    public byte[] RawPayload { get; }

    /// <summary>The IP address of the remote endpoint that sent this telegram.</summary>
    public string RemoteAddress { get; }

    /// <summary>The port of the remote endpoint that sent this telegram.</summary>
    public int Port { get; }
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
    public UnknownTelegramEventArgs(byte[] payload, ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Payload   = payload;
        ByteOrder = byteOrder;
    }

    /// <summary>
    /// The raw wire bytes of the unrecognised message (after any framing has been stripped).
    /// </summary>
    public byte[] Payload { get; }

    /// <summary>
    /// The byte order used by the connection that received this payload.
    /// </summary>
    public ByteOrder ByteOrder { get; }

    /// <summary>
    /// The first two bytes of <see cref="Payload"/> interpreted as a <c>ushort</c>
    /// using the connection's <see cref="ByteOrder"/>, or <c>0</c> if the payload is shorter than 2 bytes.
    /// </summary>
    public ushort CandidateTelegramId =>
        Payload.Length >= 2
            ? (ByteOrder == ByteOrder.LittleEndian
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(Payload)
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(Payload))
            : (ushort)0;
}
