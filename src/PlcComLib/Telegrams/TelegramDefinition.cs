using PlcComLib.DataTypes;

namespace PlcComLib.Telegrams;

/// <summary>
/// Defines a telegram: its identity, optional description, ordered list of fields,
/// and wire byte order.
/// </summary>
public sealed class TelegramDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// 2-byte big-endian MessageId used for typed-telegram dispatch.
    /// <c>0</c> means not set (legacy untyped definitions still work).
    /// </summary>
    public ushort MessageId { get; set; }

    /// <summary>
    /// Alias for <see cref="MessageId"/>. The unique 2-byte identifier that identifies
    /// this telegram on the wire. Always transmitted big-endian as the first two bytes
    /// of every serialised payload.
    /// </summary>
    public ushort TelegramId
    {
        get => MessageId;
        set => MessageId = value;
    }

    /// <summary>
    /// Byte order used to serialise and deserialise multi-byte data fields.
    /// Defaults to <see cref="ByteOrder.BigEndian"/> (Siemens S7 wire format).
    /// The <see cref="TelegramId"/> / MessageId header is always big-endian, regardless of this setting.
    /// </summary>
    public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;

    public List<TelegramField> Fields { get; set; } = [];
    public int TotalWireSize => Fields.Sum(f => f.WireSize);
}
