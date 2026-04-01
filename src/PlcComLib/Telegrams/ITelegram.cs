namespace PlcComLib.Telegrams;

/// <summary>
/// Marker interface for all telegrams that carry a unique identifier on the wire.
/// Both source-generated typed telegrams and hand-crafted untyped telegrams
/// implement this interface.
/// </summary>
public interface ITelegram
{
    /// <summary>
    /// The 2-byte telegram identifier that is prepended to every
    /// serialised payload and used to dispatch incoming messages to the correct
    /// handler. The byte order of this identifier on the wire follows the
    /// connection's <c>ByteOrder</c> setting.
    /// </summary>
    ushort TelegramId { get; }

    /// <summary>
    /// The 2-byte message identifier used for message framing and dispatch.
    /// In source-generated telegrams the value is taken from the <c>[MsgId]</c>
    /// attribute; the decorated property can have any name.
    /// </summary>
    ushort MessageId { get; }

    /// <summary>
    /// Total wire size of the serialised telegram payload in bytes, including
    /// the 2-byte message-id header. Used for message framing.
    /// In source-generated telegrams this is backed by the property decorated
    /// with <c>[MsgLength]</c>; the decorated property can have any name.
    /// </summary>
    int Length { get; }
}
