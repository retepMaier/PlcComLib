using PlcComLib.DataTypes;

namespace PlcComLib.Telegrams;

/// <summary>
/// Serialises and deserialises <see cref="Telegram"/> instances to/from their wire representation.
/// The byte order for multi-byte fields is supplied by the caller (typically the client or server).
/// </summary>
/// <remarks>
/// Definitions built from an <see cref="S7TelegramBase{TSelf}"/> type (<see cref="TelegramDefinition.UsesPlcLayout"/>)
/// are read and written at their PLC-aligned offsets, with Bool fields packed into bits — exactly the
/// layout produced by the typed serializer. Hand-crafted definitions are laid out back-to-back.
/// </remarks>
public static class TelegramSerializer
{
    public static byte[] Serialize(Telegram telegram, ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        ArgumentNullException.ThrowIfNull(telegram);
        var def = telegram.Definition;
        var buffer = new byte[def.EffectiveWireSize];
        int offset = 0;
        foreach (var field in def.Fields)
        {
            int size  = field.WireSize;
            int start = def.UsesPlcLayout ? field.PlcOffset : offset;
            if (start + size > buffer.Length)
                throw new InvalidOperationException(
                    $"Field '{field.Name}' (offset {start}, {size} bytes) does not fit into the {buffer.Length}-byte telegram '{def.Id}'.");

            var value = telegram.GetValue(field.Name);
            var slot  = buffer.AsSpan(start, size);
            switch (field.DataType)
            {
                case S7DataType.Bool when def.UsesPlcLayout && field.BitIndex >= 0:
                    if (Convert.ToBoolean(value)) slot[0] |= (byte)(1 << field.BitIndex);
                    break;
                case S7DataType.Raw:
                {
                    var raw = value as byte[] ?? throw new ArgumentException($"Raw field '{field.Name}' requires a byte[] value.");
                    raw.AsSpan(0, Math.Min(raw.Length, size)).CopyTo(slot);
                    break;
                }
                case S7DataType.CharArray:
                    WriteCharArray(value, slot);
                    break;
                default:
                    var fieldBytes = S7TypeConverter.Serialize(field.DataType, value, field.MaxStringLength, byteOrder);
                    fieldBytes.AsSpan(0, Math.Min(fieldBytes.Length, size)).CopyTo(slot);
                    break;
            }
            offset += size;
        }
        return buffer;
    }

    public static Telegram Deserialize(TelegramDefinition definition, ReadOnlySpan<byte> data, ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var telegram = new Telegram(definition);
        int offset = 0;
        foreach (var field in definition.Fields)
        {
            int size  = field.WireSize;
            int start = definition.UsesPlcLayout ? field.PlcOffset : offset;
            if (start + size > data.Length)
                throw new InvalidDataException($"Buffer too short at field '{field.Name}'. Offset={start}, Required={size}, Available={data.Length}.");

            var slot = data.Slice(start, size);
            object value = field.DataType switch
            {
                S7DataType.Bool when definition.UsesPlcLayout && field.BitIndex >= 0
                                     => (slot[0] & (1 << field.BitIndex)) != 0,
                S7DataType.CharArray => ReadCharArray(slot),
                _                    => S7TypeConverter.Deserialize(field.DataType, slot, byteOrder),
            };
            telegram.SetValue(field.Name, value);
            offset += size;
        }
        return telegram;
    }

    // ARRAY OF CHAR: one byte per character, no length header, unused tail is zero-filled.
    private static string ReadCharArray(ReadOnlySpan<byte> slot)
    {
        int end = slot.Length;
        while (end > 0 && slot[end - 1] == 0) end--;
        var chars = new char[end];
        for (int i = 0; i < end; i++) chars[i] = (char)slot[i];
        return new string(chars);
    }

    private static void WriteCharArray(object value, Span<byte> slot)
    {
        ReadOnlySpan<char> chars = value switch
        {
            char[] array => array,
            string text  => text,
            _            => value.ToString(),
        };
        int count = Math.Min(chars.Length, slot.Length);
        for (int i = 0; i < count; i++) slot[i] = (byte)chars[i];
    }
}
