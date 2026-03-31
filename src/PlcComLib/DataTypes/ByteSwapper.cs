using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace PlcComLib.DataTypes;

/// <summary>
/// Utility for converting between host byte order (little-endian on x86/x64)
/// and Siemens S7 byte order (big-endian).
/// </summary>
public static class ByteSwapper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort SwapUInt16(ushort value) => BinaryPrimitives.ReverseEndianness(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short SwapInt16(short value) => BinaryPrimitives.ReverseEndianness(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SwapUInt32(uint value) => BinaryPrimitives.ReverseEndianness(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SwapInt32(int value) => BinaryPrimitives.ReverseEndianness(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SwapUInt64(ulong value) => BinaryPrimitives.ReverseEndianness(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SwapInt64(long value) => BinaryPrimitives.ReverseEndianness(value);

    public static void WriteReal(Span<byte> destination, float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        BinaryPrimitives.WriteUInt32BigEndian(destination, bits);
    }

    public static float ReadReal(ReadOnlySpan<byte> source)
    {
        var bits = BinaryPrimitives.ReadUInt32BigEndian(source);
        return BitConverter.UInt32BitsToSingle(bits);
    }

    public static void WriteLReal(Span<byte> destination, double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        BinaryPrimitives.WriteUInt64BigEndian(destination, bits);
    }

    public static double ReadLReal(ReadOnlySpan<byte> source)
    {
        var bits = BinaryPrimitives.ReadUInt64BigEndian(source);
        return BitConverter.UInt64BitsToDouble(bits);
    }

    public static void Write(Span<byte> destination, S7DataType dataType, object value,
        ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        bool le = byteOrder == ByteOrder.LittleEndian;
        switch (dataType)
        {
            case S7DataType.Bool:
            case S7DataType.Byte:
            case S7DataType.USInt:
                destination[0] = Convert.ToByte(value);
                break;
            case S7DataType.SInt:
                destination[0] = unchecked((byte)(sbyte)Convert.ToSByte(value));
                break;
            case S7DataType.Char:
                destination[0] = (byte)Convert.ToChar(value);
                break;
            case S7DataType.Word:
            case S7DataType.UInt:
                if (le) BinaryPrimitives.WriteUInt16LittleEndian(destination, Convert.ToUInt16(value));
                else    BinaryPrimitives.WriteUInt16BigEndian(destination, Convert.ToUInt16(value));
                break;
            case S7DataType.Int:
            case S7DataType.Date:
                if (le) BinaryPrimitives.WriteInt16LittleEndian(destination, Convert.ToInt16(value));
                else    BinaryPrimitives.WriteInt16BigEndian(destination, Convert.ToInt16(value));
                break;
            case S7DataType.WChar:
                if (le) BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)Convert.ToChar(value));
                else    BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)Convert.ToChar(value));
                break;
            case S7DataType.DWord:
            case S7DataType.UDInt:
            case S7DataType.TimeOfDay:
                if (le) BinaryPrimitives.WriteUInt32LittleEndian(destination, Convert.ToUInt32(value));
                else    BinaryPrimitives.WriteUInt32BigEndian(destination, Convert.ToUInt32(value));
                break;
            case S7DataType.DInt:
            case S7DataType.Time:
                if (le) BinaryPrimitives.WriteInt32LittleEndian(destination, Convert.ToInt32(value));
                else    BinaryPrimitives.WriteInt32BigEndian(destination, Convert.ToInt32(value));
                break;
            case S7DataType.Real:
                if (le)
                    BinaryPrimitives.WriteUInt32LittleEndian(destination,
                        BitConverter.SingleToUInt32Bits(Convert.ToSingle(value)));
                else
                    WriteReal(destination, Convert.ToSingle(value));
                break;
            case S7DataType.LWord:
            case S7DataType.ULInt:
                if (le) BinaryPrimitives.WriteUInt64LittleEndian(destination, Convert.ToUInt64(value));
                else    BinaryPrimitives.WriteUInt64BigEndian(destination, Convert.ToUInt64(value));
                break;
            case S7DataType.LInt:
                if (le) BinaryPrimitives.WriteInt64LittleEndian(destination, Convert.ToInt64(value));
                else    BinaryPrimitives.WriteInt64BigEndian(destination, Convert.ToInt64(value));
                break;
            case S7DataType.LReal:
                if (le)
                    BinaryPrimitives.WriteUInt64LittleEndian(destination,
                        BitConverter.DoubleToUInt64Bits(Convert.ToDouble(value)));
                else
                    WriteLReal(destination, Convert.ToDouble(value));
                break;
            default:
                throw new NotSupportedException($"Use the dedicated S7String/DateAndTime writer for {dataType}.");
        }
    }

    public static object Read(ReadOnlySpan<byte> source, S7DataType dataType,
        ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        bool le = byteOrder == ByteOrder.LittleEndian;
        return dataType switch
        {
            S7DataType.Bool   => source[0] != 0,
            S7DataType.Byte   => source[0],
            S7DataType.USInt  => source[0],
            S7DataType.SInt   => (sbyte)source[0],
            S7DataType.Char   => (char)source[0],
            S7DataType.Word   => le ? BinaryPrimitives.ReadUInt16LittleEndian(source) : BinaryPrimitives.ReadUInt16BigEndian(source),
            S7DataType.UInt   => le ? BinaryPrimitives.ReadUInt16LittleEndian(source) : BinaryPrimitives.ReadUInt16BigEndian(source),
            S7DataType.Int    => le ? BinaryPrimitives.ReadInt16LittleEndian(source)  : BinaryPrimitives.ReadInt16BigEndian(source),
            S7DataType.Date   => le ? BinaryPrimitives.ReadInt16LittleEndian(source)  : BinaryPrimitives.ReadInt16BigEndian(source),
            S7DataType.WChar  => le ? (char)BinaryPrimitives.ReadUInt16LittleEndian(source) : (char)BinaryPrimitives.ReadUInt16BigEndian(source),
            S7DataType.DWord  => le ? BinaryPrimitives.ReadUInt32LittleEndian(source) : BinaryPrimitives.ReadUInt32BigEndian(source),
            S7DataType.UDInt  => le ? BinaryPrimitives.ReadUInt32LittleEndian(source) : BinaryPrimitives.ReadUInt32BigEndian(source),
            S7DataType.TimeOfDay => le ? BinaryPrimitives.ReadUInt32LittleEndian(source) : BinaryPrimitives.ReadUInt32BigEndian(source),
            S7DataType.DInt   => le ? BinaryPrimitives.ReadInt32LittleEndian(source)  : BinaryPrimitives.ReadInt32BigEndian(source),
            S7DataType.Time   => le ? BinaryPrimitives.ReadInt32LittleEndian(source)  : BinaryPrimitives.ReadInt32BigEndian(source),
            S7DataType.Real   => le
                ? BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(source))
                : ReadReal(source),
            S7DataType.LWord  => le ? BinaryPrimitives.ReadUInt64LittleEndian(source) : BinaryPrimitives.ReadUInt64BigEndian(source),
            S7DataType.ULInt  => le ? BinaryPrimitives.ReadUInt64LittleEndian(source) : BinaryPrimitives.ReadUInt64BigEndian(source),
            S7DataType.LInt   => le ? BinaryPrimitives.ReadInt64LittleEndian(source)  : BinaryPrimitives.ReadInt64BigEndian(source),
            S7DataType.LReal  => le
                ? BitConverter.UInt64BitsToDouble(BinaryPrimitives.ReadUInt64LittleEndian(source))
                : ReadLReal(source),
            _ => throw new NotSupportedException($"Use the dedicated S7String/DateAndTime reader for {dataType}.")
        };
    }

    public static int GetByteSize(S7DataType dataType) => dataType switch
    {
        S7DataType.Bool or S7DataType.Byte or S7DataType.USInt or S7DataType.SInt or S7DataType.Char => 1,
        S7DataType.Word or S7DataType.UInt or S7DataType.Int or S7DataType.Date or S7DataType.WChar => 2,
        S7DataType.DWord or S7DataType.UDInt or S7DataType.DInt or S7DataType.Time or S7DataType.TimeOfDay or S7DataType.Real => 4,
        S7DataType.LWord or S7DataType.ULInt or S7DataType.LInt or S7DataType.LReal => 8,
        S7DataType.DateAndTime => 8,
        S7DataType.S7String or S7DataType.S7WString or S7DataType.Raw => 0,
        _ => 0
    };
}
