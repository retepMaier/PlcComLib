using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>S7 BYTE / USINT (1-byte unsigned). Assign and read as <see cref="byte"/>.</summary>
public readonly struct S7Byte(byte v) : IS7FramingType
{
    private readonly byte _v = v;

    public static S7DataType DataType => S7DataType.Byte;
    public static int WireSize => 1;
    public static implicit operator S7Byte(byte v) => new(v);
    public static implicit operator byte(S7Byte s)  => s._v;
    public override string ToString() => _v.ToString();
}
