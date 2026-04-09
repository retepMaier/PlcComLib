using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

// ── 2-byte types ─────────────────────────────────────────────────────────────

/// <summary>S7 WORD / UINT (2-byte unsigned). Assign and read as <see cref="ushort"/>.</summary>
public readonly struct S7Word(ushort v) : IS7FramingType
{
    private readonly ushort _v = v;

    public static S7DataType DataType => S7DataType.Word;
    public static int WireSize => 2;
    public static implicit operator S7Word(ushort v) => new(v);
    public static implicit operator ushort(S7Word s)  => s._v;
    public override string ToString() => _v.ToString();
}
