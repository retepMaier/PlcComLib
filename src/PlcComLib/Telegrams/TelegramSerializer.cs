using PlcComLib.DataTypes;

namespace PlcComLib.Telegrams;

/// <summary>
/// Serialises and deserialises Telegram instances to/from their S7 wire representation.
/// The byte order used for multi-byte fields is taken from <see cref="TelegramDefinition.ByteOrder"/>.
/// </summary>
public static class TelegramSerializer
{
    public static byte[] Serialize(Telegram telegram)
    {
        ArgumentNullException.ThrowIfNull(telegram);
        var def = telegram.Definition;
        var byteOrder = def.ByteOrder;
        int totalSize = def.TotalWireSize;
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

    public static Telegram Deserialize(TelegramDefinition definition, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var byteOrder = definition.ByteOrder;
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
