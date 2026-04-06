using PlcComLib.DataTypes;

namespace PlcComLib.Telegrams;

/// <summary>
/// Defines a single field within a telegram.
/// </summary>
public sealed class TelegramField
{
    public string Name { get; set; } = string.Empty;
    public S7DataType DataType { get; set; }
    public byte MaxStringLength { get; set; }
    public int RawByteCount { get; set; }
    public string? Description { get; set; }
    public int WireSize => S7TypeConverter.GetWireSize(DataType, MaxStringLength, RawByteCount);

    /// <summary>Byte offset of this field in the PLC-aligned data block.</summary>
    public int PlcOffset { get; set; }

    /// <summary>Bit index (0–7) for Bool fields packed into a shared byte. -1 for all other types.</summary>
    public int BitIndex { get; set; } = -1;
}
