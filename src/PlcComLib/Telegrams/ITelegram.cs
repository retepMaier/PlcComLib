namespace PlcComLib.Telegrams;

/// <summary>
/// Marker interface for all telegrams that carry a unique identifier on the wire.
/// Both source-generated typed telegrams and hand-crafted untyped telegrams
/// implement this interface.
/// </summary>
public interface ITelegram
{
    /// <summary>
    /// The 2-byte big-endian telegram identifier that is prepended to every
    /// serialised payload and used to dispatch incoming messages to the correct
    /// handler.
    /// </summary>
    ushort TelegramId { get; }
}
