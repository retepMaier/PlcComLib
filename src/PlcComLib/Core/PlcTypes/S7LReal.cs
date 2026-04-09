using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>S7 LREAL (8-byte IEEE 754 double). Assign and read as <see cref="double"/>.</summary>
public readonly struct S7LReal(double v) : IS7FramingType
{
    private readonly double _v = v;

    public static S7DataType DataType => S7DataType.LReal;
    public static int WireSize => 8;
    public static implicit operator S7LReal(double v) => new(v);
    public static implicit operator double(S7LReal s)  => s._v;
    public override string ToString() => _v.ToString();
}
