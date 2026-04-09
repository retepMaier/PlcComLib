using PlcComLib.DataTypes;

namespace PlcComLib.Core.PlcTypes;

// ── 1-byte types ─────────────────────────────────────────────────────────────

/// <summary>S7 BOOL (1 byte on wire). Assign and read as <see cref="bool"/>.</summary>
public readonly struct S7Bool(bool v) : IS7FramingType
{
    private readonly bool _v = v;

    public static S7DataType DataType => S7DataType.Bool;
    public static int WireSize => 1;
    public static implicit operator S7Bool(bool v) => new(v);
    public static implicit operator bool(S7Bool s)  => s._v;
    public override string ToString() => _v.ToString();
}
