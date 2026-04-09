using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>S7 CHAR (1-byte ASCII character). Assign and read as <see cref="char"/>.</summary>
public readonly struct S7Char(char v) : IS7FramingType
{
    private readonly char _v = v;

    public static S7DataType DataType => S7DataType.Char;
    public static int WireSize => 1;
    public static implicit operator S7Char(char v) => new(v);
    public static implicit operator char(S7Char s)  => s._v;
    public override string ToString() => _v.ToString();
}
