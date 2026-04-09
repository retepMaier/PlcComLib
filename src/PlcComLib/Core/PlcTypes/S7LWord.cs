using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

// ── 8-byte types ─────────────────────────────────────────────────────────────

/// <summary>S7 LWORD / ULINT (8-byte unsigned). Assign and read as <see cref="ulong"/>.</summary>
public readonly struct S7LWord(ulong v) : IS7FramingType
{
    private readonly ulong _v = v;

    public static S7DataType DataType => S7DataType.LWord;
    public static int WireSize => 8;
    public static implicit operator S7LWord(ulong v) => new(v);
    public static implicit operator ulong(S7LWord s)  => s._v;
    public override string ToString() => _v.ToString();
}
