using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>S7 INT (2-byte signed). Assign and read as <see cref="short"/>.</summary>
public readonly struct S7Int(short v) : IS7FramingType
{
    private readonly short _v = v;

    public static S7DataType DataType => S7DataType.Int;
    public static int WireSize => 2;
    public static implicit operator S7Int(short v) => new(v);
    public static implicit operator short(S7Int s)  => s._v;
    public override string ToString() => _v.ToString();
}
