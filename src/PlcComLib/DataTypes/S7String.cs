using System.Text;

namespace PlcComLib.DataTypes;
// ── plain S7String / S7WString kept for internal use by the serialiser ───────


/// <summary>
/// Represents a Siemens S7 STRING (single-byte character string).
/// Wire format: [MaxLen:BYTE][ActualLen:BYTE][Characters:BYTE*MaxLen]
/// </summary>
public sealed class S7String
{
    public static readonly Encoding S7Encoding = Encoding.Latin1;

    public byte MaxLength { get; }
    public string Value { get; }

    public S7String(string value, byte maxLength = 254)
    {
        if (maxLength > 254) throw new ArgumentOutOfRangeException(nameof(maxLength), "S7 STRING max length is 254.");
        var encoded = S7Encoding.GetByteCount(value);
        if (encoded > maxLength)
            throw new ArgumentException($"Encoded string length {encoded} exceeds declared max length {maxLength}.");
        Value = value;
        MaxLength = maxLength;
    }

    public int WireSize => 2 + MaxLength;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < WireSize)
            throw new ArgumentException($"Destination too small. Required {WireSize}, got {destination.Length}.");
        var encoded = S7Encoding.GetBytes(Value);
        destination[0] = MaxLength;
        destination[1] = (byte)encoded.Length;
        encoded.CopyTo(destination[2..]);
        destination[(2 + encoded.Length)..WireSize].Clear();
    }

    public static S7String ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < 2) throw new ArgumentException("Buffer too short for S7 STRING header.");
        byte maxLen = source[0];
        byte actualLen = source[1];
        if (source.Length < 2 + maxLen)
            throw new ArgumentException($"Buffer too short. Expected {2 + maxLen} bytes, got {source.Length}.");
        if (actualLen > maxLen)
            throw new InvalidDataException($"S7 STRING actual length {actualLen} exceeds max length {maxLen}.");
        var str = S7Encoding.GetString(source.Slice(2, actualLen));
        return new S7String(str, maxLen);
    }

    public override string ToString() => Value;
    public override bool Equals(object? obj) => obj is S7String s && s.Value == Value && s.MaxLength == MaxLength;
    public override int GetHashCode() => HashCode.Combine(MaxLength, Value);
}

/// <summary>
/// Represents a Siemens S7 WSTRING (wide-character string, UCS-2/UTF-16BE).
/// Wire format: [MaxLen:WORD][ActualLen:WORD][Characters:WORD*MaxLen]
/// </summary>
public sealed class S7WString
{
    public static readonly Encoding S7WEncoding = Encoding.BigEndianUnicode;

    public ushort MaxLength { get; }
    public string Value { get; }

    public S7WString(string value, ushort maxLength = 254)
    {
        if (maxLength > 16382) throw new ArgumentOutOfRangeException(nameof(maxLength), "S7 WSTRING max length is 16382.");
        if (value.Length > maxLength)
            throw new ArgumentException($"String length {value.Length} exceeds declared max length {maxLength}.");
        Value = value;
        MaxLength = maxLength;
    }

    public int WireSize => 4 + MaxLength * 2;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < WireSize)
            throw new ArgumentException($"Destination too small. Required {WireSize}, got {destination.Length}.");
        var encoded = S7WEncoding.GetBytes(Value);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(destination, MaxLength);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)Value.Length);
        encoded.CopyTo(destination[4..]);
        destination[(4 + encoded.Length)..WireSize].Clear();
    }

    public static S7WString ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < 4) throw new ArgumentException("Buffer too short for S7 WSTRING header.");
        ushort maxLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(source);
        ushort actualLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(source[2..]);
        int expectedSize = 4 + maxLen * 2;
        if (source.Length < expectedSize)
            throw new ArgumentException($"Buffer too short. Expected {expectedSize} bytes, got {source.Length}.");
        if (actualLen > maxLen)
            throw new InvalidDataException($"S7 WSTRING actual length {actualLen} exceeds max length {maxLen}.");
        var str = S7WEncoding.GetString(source.Slice(4, actualLen * 2));
        return new S7WString(str, maxLen);
    }

    public override string ToString() => Value;
    public override bool Equals(object? obj) => obj is S7WString s && s.Value == Value && s.MaxLength == MaxLength;
    public override int GetHashCode() => HashCode.Combine(MaxLength, Value);
}

// ── Generic value structs (Option A) ─────────────────────────────────────────

/// <summary>
/// S7 STRING with a compile-time maximum length baked into the type.
/// Assign and read as <see cref="string"/> via implicit operators.
/// <code>public S7String&lt;L10&gt; Label { get; set; } = "hello";</code>
/// </summary>
/// <typeparam name="TLen">
/// One of the pre-defined length structs (<see cref="L10"/>, <see cref="L32"/>, …) or a
/// custom struct implementing <see cref="IS7Length"/>.
/// </typeparam>
public readonly struct S7String<TLen> : IS7FramingType
    where TLen : struct, IS7Length
{
    private readonly string? _v;

    public S7String(string value) => _v = value;

    /// <summary>The string value. Never null — returns <see cref="string.Empty"/> for default instances.</summary>
    public string Value => _v ?? string.Empty;

    /// <summary>Maximum number of characters as declared by <typeparamref name="TLen"/>.</summary>
    public static int MaxLength => TLen.Value;

    public static S7DataType DataType => S7DataType.S7String;

    /// <summary>Wire size in bytes: 2-byte header + <see cref="MaxLength"/> character bytes.</summary>
    public static int WireSize => 2 + TLen.Value;

    public static implicit operator S7String<TLen>(string v) => new(v);
    public static implicit operator string(S7String<TLen> s) => s._v ?? string.Empty;

    public override string ToString() => _v ?? string.Empty;
}

/// <summary>
/// S7 WSTRING with a compile-time maximum length baked into the type.
/// Assign and read as <see cref="string"/> via implicit operators.
/// <code>public S7WString&lt;L20&gt; Title { get; set; } = "Maschine";</code>
/// </summary>
/// <typeparam name="TLen">
/// One of the pre-defined length structs (<see cref="L10"/>, <see cref="L32"/>, …) or a
/// custom struct implementing <see cref="IS7Length"/>.
/// </typeparam>
public readonly struct S7WString<TLen> : IS7FramingType
    where TLen : struct, IS7Length
{
    private readonly string? _v;

    public S7WString(string value) => _v = value;

    /// <summary>The string value. Never null — returns <see cref="string.Empty"/> for default instances.</summary>
    public string Value => _v ?? string.Empty;

    /// <summary>Maximum number of characters as declared by <typeparamref name="TLen"/>.</summary>
    public static int MaxLength => TLen.Value;

    public static S7DataType DataType => S7DataType.S7WString;

    /// <summary>Wire size in bytes: 4-byte header + <see cref="MaxLength"/> × 2 character bytes.</summary>
    public static int WireSize => 4 + TLen.Value * 2;

    public static implicit operator S7WString<TLen>(string v) => new(v);
    public static implicit operator string(S7WString<TLen> s) => s._v ?? string.Empty;

    public override string ToString() => _v ?? string.Empty;
}
