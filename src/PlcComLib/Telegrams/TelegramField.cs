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
}
