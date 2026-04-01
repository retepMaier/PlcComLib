using PlcComLib.DataTypes;

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
    /// 2-byte MessageId used for typed-telegram dispatch and as the first two bytes of
    /// every serialised payload. Set at registration time via
    /// <c>RegisterTelegram&lt;T&gt;().WithMessageId(id)</c> on the connection builder.
    /// <c>0</c> means not set (legacy untyped definitions still work via size-based matching).
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

    /// <summary>
    /// Byte offset in the received payload at which the MessageId value is read.
    /// Default is <c>0</c> (first two bytes of the payload).
    /// Configure via <c>.WithMessageId&lt;TType&gt;(id, byteOffset)</c> on the connection builder.
    /// </summary>
    public int MessageIdByteOffset { get; set; } = 0;

    /// <summary>
    /// S7 data type used to read the MessageId from the payload at <see cref="MessageIdByteOffset"/>.
    /// Default is <see cref="S7DataType.Word"/> (unsigned 16-bit).
    /// </summary>
    public S7DataType MessageIdDataType { get; set; } = S7DataType.Word;

    /// <summary>
    /// Byte offset in the received payload at which the length field is read for validation.
    /// <c>-1</c> (default) means no length-field validation is performed.
    /// Configure via <c>.WithLength&lt;TType&gt;(length, byteOffset)</c> on the connection builder.
    /// </summary>
    public int LengthByteOffset { get; set; } = -1;

    /// <summary>
    /// S7 data type used to read the length field from the payload at <see cref="LengthByteOffset"/>.
    /// Default is <see cref="S7DataType.Word"/> (unsigned 16-bit).
    /// </summary>
    public S7DataType LengthDataType { get; set; } = S7DataType.Word;

    public List<TelegramField> Fields { get; set; } = [];

    /// <summary>
    /// Total wire size derived from the declared field list.
    /// For source-generated telegrams this always equals <see cref="ConfiguredWireSize"/>.
    /// </summary>
    public int TotalWireSize => Fields.Sum(f => f.WireSize);

    /// <summary>
    /// Wire size explicitly configured via <c>.WithLength(size)</c> on the connection builder.
    /// When non-zero, framers prefer this value over <see cref="TotalWireSize"/>.
    /// Useful for hand-crafted definitions whose <see cref="Fields"/> list is empty.
    /// </summary>
    public int ConfiguredWireSize { get; set; }

    /// <summary>
    /// Effective wire size used by framers: <see cref="ConfiguredWireSize"/> when non-zero,
    /// otherwise <see cref="TotalWireSize"/>.
    /// </summary>
    public int EffectiveWireSize => ConfiguredWireSize > 0 ? ConfiguredWireSize : TotalWireSize;
}
