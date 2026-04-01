namespace PlcComLib.Telegrams;

/// <summary>
/// Marker interface for all telegrams that carry a unique identifier on the wire.
/// Both source-generated typed telegrams and hand-crafted untyped telegrams
/// implement this interface.
/// </summary>
public interface ITelegram
{
    /// <summary>
    /// The telegram identifier configured via <c>RegisterTelegram&lt;T&gt;().WithMessageId(id)</c>
    /// on the connection builder. Returns <c>0</c> until registration.
    /// </summary>
    ushort TelegramId { get; }
}
