namespace PlcComLib.Telegrams;

/// <summary>
/// Extension methods for <see cref="S7TelegramBase{TSelf}"/> instances.
/// </summary>
public static class S7TelegramExtensions
{
    /// <summary>
    /// Serialises the telegram to a <see cref="S7DataBlock"/> — a byte array with named,
    /// PLC-offset-indexed field access — using the S7-aligned wire layout.
    /// </summary>
    /// <param name="telegram">The telegram to serialise.</param>
    /// <param name="byteOrder">Byte order (default: BigEndian for Siemens S7).</param>
    public static S7DataBlock ToDataBlock<TSelf>(
        this S7TelegramBase<TSelf> telegram,
        PlcComLib.DataTypes.ByteOrder byteOrder = PlcComLib.DataTypes.ByteOrder.BigEndian)
        where TSelf : S7TelegramBase<TSelf>, new()
    {
        var bytes  = telegram.Serialize(byteOrder);
        var fields = S7TelegramBase<TSelf>.Definition.Fields;
        return new S7DataBlock(bytes, fields);
    }
}
