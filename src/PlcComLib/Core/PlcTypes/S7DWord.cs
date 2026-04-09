using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

// ── 4-byte types ─────────────────────────────────────────────────────────────

/// <summary>S7 DWORD / UDINT (4-byte unsigned). Assign and read as <see cref="uint"/>.</summary>
public readonly struct S7DWord(uint v) : IS7FramingType
{
    private readonly uint _v = v;

    public static S7DataType DataType => S7DataType.DWord;
    public static int WireSize => 4;
    public static implicit operator S7DWord(uint v) => new(v);
    public static implicit operator uint(S7DWord s)  => s._v;
    public override string ToString() => _v.ToString();
}
