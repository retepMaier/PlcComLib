using PlcComLib.Telegrams;

namespace PlcComLib.Core.Events;

/// <summary>Event args for a received telegram.</summary>
public sealed class TelegramReceivedEventArgs(Telegram telegram, byte[]? rawPayload = null, string remoteAddress = "", int port = 0) : EventArgs
{
    public Telegram Telegram { get; } = telegram;

    /// <summary>The raw wire bytes that produced this telegram (includes the MessageId header for typed telegrams).</summary>
    public byte[] RawPayload { get; } = rawPayload ?? [];

    /// <summary>The IP address of the remote endpoint that sent this telegram.</summary>
    public string RemoteAddress { get; } = remoteAddress;

    /// <summary>The port of the remote endpoint that sent this telegram.</summary>
    public int Port { get; } = port;
}
