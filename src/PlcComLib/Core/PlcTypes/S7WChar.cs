using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>S7 WCHAR (2-byte Unicode character). Assign and read as <see cref="char"/>.</summary>
public readonly struct S7WChar(char v) : IS7FramingType
{
    private readonly char _v = v;

    public static S7DataType DataType => S7DataType.WChar;
    public static int WireSize => 2;
    public static implicit operator S7WChar(char v) => new(v);
    public static implicit operator char(S7WChar s)  => s._v;
    public override string ToString() => _v.ToString();
}
