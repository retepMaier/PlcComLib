using PlcComLib.DataTypes;

namespace PlcComLib.Telegrams;

/// <summary>
/// Serialises and deserialises <see cref="Telegram"/> instances to/from their wire representation.
/// The byte order for multi-byte fields is supplied by the caller (typically the client or server).
/// </summary>
public static class TelegramSerializer
{
    public static byte[] Serialize(Telegram telegram,ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        ArgumentNullException.ThrowIfNull(telegram);
        var def = telegram.Definition;
        int totalSize = def.EffectiveWireSize;
        var buffer = new byte[totalSize];
        int offset = 0;
        foreach (var field in def.Fields)
        {
            var value = telegram.GetValue(field.Name);
            var fieldBytes = S7TypeConverter.Serialize(field.DataType, value, field.MaxStringLength, byteOrder);
            fieldBytes.CopyTo(buffer, offset);
            offset += fieldBytes.Length;
        }
        return buffer;
    }

    public static Telegram Deserialize(TelegramDefinition definition, ReadOnlySpan<byte> data,ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var telegram = new Telegram(definition);
        int offset = 0;
        foreach (var field in definition.Fields)
        {
            int size = field.WireSize;
            if (offset + size > data.Length)
                throw new InvalidDataException(
                    $"Buffer too short at field '{field.Name}'. Offset={offset}, Required={size}, Remaining={data.Length - offset}.");
            var value = S7TypeConverter.Deserialize(field.DataType, data.Slice(offset, size), byteOrder);
            telegram.SetValue(field.Name, value);
            offset += size;
        }
        return telegram;
    }
}
