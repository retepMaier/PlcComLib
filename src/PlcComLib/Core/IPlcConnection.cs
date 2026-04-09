using PlcComLib.Core.Events;
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
