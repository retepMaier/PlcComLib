using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>S7 DINT (4-byte signed). Assign and read as <see cref="int"/>.</summary>
public readonly struct S7DInt(int v) : IS7FramingType
{
    private readonly int _v = v;

    public static S7DataType DataType => S7DataType.DInt;
    public static int WireSize => 4;
    public static implicit operator S7DInt(int v) => new(v);
    public static implicit operator int(S7DInt s)  => s._v;
    public override string ToString() => _v.ToString();
}
