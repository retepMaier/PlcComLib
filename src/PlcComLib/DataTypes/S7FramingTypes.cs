namespace PlcComLib.DataTypes;

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

/// <summary>S7 BYTE / USINT (1-byte unsigned). Assign and read as <see cref="byte"/>.</summary>
public readonly struct S7Byte(byte v) : IS7FramingType
{
    private readonly byte _v = v;

    public static S7DataType DataType => S7DataType.Byte;
    public static int WireSize => 1;
    public static implicit operator S7Byte(byte v) => new(v);
    public static implicit operator byte(S7Byte s)  => s._v;
    public override string ToString() => _v.ToString();
}

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

/// <summary>S7 DINT (4-byte signed). Assign and read as <see cref="int"/>.</summary>
public readonly struct S7DInt(int v) : IS7FramingType
{
    private readonly int _v = v;

    public static S7DataType DataType => S7DataType.DInt;
    public static int WireSize => 4;
    public static implicit operator S7DInt(int v) => new(v);
    public static implicit operator int(S7DInt s)  => s._v;
    public override string ToString() => _v.ToString();
}

/// <summary>S7 REAL (4-byte IEEE 754 float). Assign and read as <see cref="float"/>.</summary>
public readonly struct S7Real(float v) : IS7FramingType
{
    private readonly float _v = v;

    public static S7DataType DataType => S7DataType.Real;
    public static int WireSize => 4;
    public static implicit operator S7Real(float v) => new(v);
    public static implicit operator float(S7Real s)  => s._v;
    public override string ToString() => _v.ToString();
}

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

/// <summary>S7 LREAL (8-byte IEEE 754 double). Assign and read as <see cref="double"/>.</summary>
public readonly struct S7LReal(double v) : IS7FramingType
{
    private readonly double _v = v;

    public static S7DataType DataType => S7DataType.LReal;
    public static int WireSize => 8;
    public static implicit operator S7LReal(double v) => new(v);
    public static implicit operator double(S7LReal s)  => s._v;
    public override string ToString() => _v.ToString();
}

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
