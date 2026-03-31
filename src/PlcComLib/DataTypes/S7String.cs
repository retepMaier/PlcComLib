using System.Text;

namespace PlcComLib.DataTypes;

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
