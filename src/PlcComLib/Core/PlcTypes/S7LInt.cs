using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>S7 LINT (8-byte signed). Assign and read as <see cref="long"/>.</summary>
public readonly struct S7LInt(long v) : IS7FramingType
{
    private readonly long _v = v;

    public static S7DataType DataType => S7DataType.LInt;
    public static int WireSize => 8;
    public static implicit operator S7LInt(long v) => new(v);
    public static implicit operator long(S7LInt s)  => s._v;
    public override string ToString() => _v.ToString();
}
