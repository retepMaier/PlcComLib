using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>S7 SINT (1-byte signed). Assign and read as <see cref="sbyte"/>.</summary>
public readonly struct S7SInt(sbyte v) : IS7FramingType
{
    private readonly sbyte _v = v;

    public static S7DataType DataType => S7DataType.SInt;
    public static int WireSize => 1;
    public static implicit operator S7SInt(sbyte v) => new(v);
    public static implicit operator sbyte(S7SInt s)  => s._v;
    public override string ToString() => _v.ToString();
}
