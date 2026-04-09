using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

/// <summary>
/// S7 DATE_AND_TIME (8 BCD bytes). Assign and read as <see cref="DateTime"/>.
/// </summary>
public readonly struct S7DateAndTime(DateTime v) : IS7FramingType
{
    private readonly DateTime _v = v;

    public static S7DataType DataType => S7DataType.DateAndTime;
    public static int WireSize => 8;
    public static implicit operator S7DateAndTime(DateTime v) => new(v);
    public static implicit operator DateTime(S7DateAndTime s)  => s._v;
    public override string ToString() => _v.ToString();
}
