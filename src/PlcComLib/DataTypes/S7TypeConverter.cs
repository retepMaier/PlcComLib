namespace PlcComLib.DataTypes;

/// <summary>
/// Converts .NET values to/from the Siemens S7 wire representation.
/// </summary>
public static class S7TypeConverter
{
    public static int GetWireSize(S7DataType dataType, byte maxStringLength = 0, int rawByteCount = 0)
    {
        return dataType switch
        {
            S7DataType.S7String  => 2 + maxStringLength,
            S7DataType.S7WString => 4 + maxStringLength * 2,
            S7DataType.Raw       => rawByteCount,
            S7DataType.CharArray => rawByteCount,
            _                    => ByteSwapper.GetByteSize(dataType)
        };
    }

    public static byte[] Serialize(S7DataType dataType, object value, byte maxStringLength = 0,ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        if (dataType == S7DataType.S7String)
        {
            var s7s = value is S7String s ? s : new S7String(value.ToString()!, maxStringLength);
            var buf = new byte[s7s.WireSize];
            s7s.WriteTo(buf);
            return buf;
        }
        if (dataType == S7DataType.S7WString)
        {
            var s7ws = value is S7WString sw ? sw : new S7WString(value.ToString()!, maxStringLength);
            var buf = new byte[s7ws.WireSize];
            s7ws.WriteTo(buf);
            return buf;
        }
        if (dataType == S7DataType.Raw)
        {
            return value is byte[] b ? b : throw new ArgumentException("Raw fields require byte[] value.");
        }
        if (dataType == S7DataType.DateAndTime)
        {
            return SerializeDateAndTime(Convert.ToDateTime(value));
        }
        int size = ByteSwapper.GetByteSize(dataType);
        var bytes = new byte[size];
        ByteSwapper.Write(bytes, dataType, value, byteOrder);
        return bytes;
    }

    public static object Deserialize(S7DataType dataType, ReadOnlySpan<byte> source,ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        if (dataType == S7DataType.S7String) return S7String.ReadFrom(source);
        if (dataType == S7DataType.S7WString) return S7WString.ReadFrom(source);
        if (dataType == S7DataType.Raw) return source.ToArray();
        if (dataType == S7DataType.DateAndTime) return DeserializeDateAndTime(source);
        return ByteSwapper.Read(source, dataType, byteOrder);
    }

    private static byte ToBcd(int value) => (byte)((value / 10 << 4) | (value % 10));
    private static int FromBcd(byte value) => (value >> 4) * 10 + (value & 0x0F);

    private static byte[] SerializeDateAndTime(DateTime dt)
    {
        var buf = new byte[8];
        buf[0] = ToBcd(dt.Year % 100);
        buf[1] = ToBcd(dt.Month);
        buf[2] = ToBcd(dt.Day);
        buf[3] = ToBcd(dt.Hour);
        buf[4] = ToBcd(dt.Minute);
        buf[5] = ToBcd(dt.Second);
        int ms = dt.Millisecond;
        buf[6] = (byte)((ToBcd(ms / 10) & 0xFF));
        buf[7] = (byte)((ms % 10 << 4) | ((int)dt.DayOfWeek + 1));
        return buf;
    }

    private static DateTime DeserializeDateAndTime(ReadOnlySpan<byte> source)
    {
        if (source.Length < 8) throw new ArgumentException("DATE_AND_TIME requires 8 bytes.");
        int year = FromBcd(source[0]);
        year += year >= 90 ? 1900 : 2000;
        int month = FromBcd(source[1]);
        int day = FromBcd(source[2]);
        int hour = FromBcd(source[3]);
        int minute = FromBcd(source[4]);
        int second = FromBcd(source[5]);
        int msHigh = FromBcd(source[6]);
        int msLow = source[7] >> 4;
        int ms = msHigh * 10 + msLow;
        return new DateTime(year, month, day, hour, minute, second, ms);
    }
}
