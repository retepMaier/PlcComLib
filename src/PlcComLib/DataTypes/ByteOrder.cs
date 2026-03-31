namespace PlcComLib.DataTypes;

/// <summary>
/// Specifies the byte order used when serialising telegram data fields.
/// </summary>
public enum ByteOrder
{
    /// <summary>Big-endian (Siemens S7 PLC wire format). This is the default.</summary>
    BigEndian = 0,

    /// <summary>Little-endian (Windows/Linux devices, non-S7 targets).</summary>
    LittleEndian = 1,
}
