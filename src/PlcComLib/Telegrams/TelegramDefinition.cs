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
    /// Numeric MessageId used for typed-telegram dispatch.
    /// Set at registration time via <c>RegisterTelegram&lt;T&gt;().WithMessageId(id)</c>
    /// on the connection builder.
    /// The storage type is <see cref="long"/> so that 1-, 2-, 4- and 8-byte S7 field types
    /// (e.g. Byte, Word, DWord, LWord and their signed equivalents) can all be used as the
    /// id discriminator without truncation.
    /// <c>0</c> means not set — legacy untyped definitions are matched by wire size instead.
    /// </summary>
    public long MessageId { get; set; }

    /// <summary>
    /// Alias for <see cref="MessageId"/>. The numeric identifier that identifies
    /// this telegram type on the wire.
    /// </summary>
    public long TelegramId
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

    /// <summary>
    /// PLC-aligned wire size of a definition built from an <see cref="S7TelegramBase{TSelf}"/> type
    /// (even-byte alignment and bool packing applied). <c>0</c> for hand-crafted definitions, whose
    /// fields are laid out back-to-back in declaration order.
    /// </summary>
    public int LayoutWireSize { get; init; }

    /// <summary>
    /// <c>true</c> when the fields use the PLC-aligned layout described by
    /// <see cref="TelegramField.PlcOffset"/> and <see cref="TelegramField.BitIndex"/>.
    /// </summary>
    public bool UsesPlcLayout => LayoutWireSize > 0;

    /// <summary>
    /// Returns a deep copy of this definition. Builders register a copy of a typed telegram's
    /// shared <c>T.Definition</c> so that per-connection settings (MessageId, length field, …)
    /// never leak into other connections.
    /// </summary>
    public TelegramDefinition Clone() => new()
    {
        Id                  = Id,
        Name                = Name,
        Description         = Description,
        MessageId           = MessageId,
        MessageIdByteOffset = MessageIdByteOffset,
        MessageIdDataType   = MessageIdDataType,
        LengthByteOffset    = LengthByteOffset,
        LengthDataType      = LengthDataType,
        ConfiguredWireSize  = ConfiguredWireSize,
        LayoutWireSize      = LayoutWireSize,
        Fields = Fields.Select(static f => new TelegramField
        {
            Name            = f.Name,
            DataType        = f.DataType,
            MaxStringLength = f.MaxStringLength,
            RawByteCount    = f.RawByteCount,
            Description     = f.Description,
            PlcOffset       = f.PlcOffset,
            BitIndex        = f.BitIndex,
        }).ToList(),
    };
}
