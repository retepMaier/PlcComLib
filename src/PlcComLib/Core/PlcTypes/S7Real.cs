using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>S7 REAL (4-byte IEEE 754 float). Assign and read as <see cref="float"/>.</summary>
public readonly struct S7Real(float v) : IS7FramingType
{
    private readonly float _v = v;

    public static S7DataType DataType => S7DataType.Real;
    public static int WireSize => 4;
    public static implicit operator S7Real(float v) => new(v);
    public static implicit operator float(S7Real s)  => s._v;
    public override string ToString() => _v.ToString();
}
