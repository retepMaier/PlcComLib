namespace PlcComLib.Telegrams;

/// <summary>
/// Defines a telegram: its identity, optional description, and ordered list of fields.
/// The byte order for multi-byte fields is determined by the client/server connection
/// configuration (<c>WithByteOrder()</c>) — not by the definition itself.
/// </summary>
public sealed class TelegramDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// 2-byte MessageId used for typed-telegram dispatch.
    /// <c>0</c> means not set (legacy untyped definitions still work via size-based matching).
    /// Transmitted as the first two bytes of every serialised payload using the connection's byte order.
    /// </summary>
    public ushort MessageId { get; set; }

    /// <summary>
    /// Alias for <see cref="MessageId"/>. The unique 2-byte identifier that identifies
    /// this telegram type on the wire.
    /// </summary>
    public ushort TelegramId
    {
        get => MessageId;
        set => MessageId = value;
    }

    public List<TelegramField> Fields { get; set; } = [];
    public int TotalWireSize => Fields.Sum(f => f.WireSize);
}
