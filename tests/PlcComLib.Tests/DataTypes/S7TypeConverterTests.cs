using FluentAssertions;
using PlcComLib.Core.PlcTypes;
using PlcComLib.DataTypes;
using Xunit;

namespace PlcComLib.Tests.DataTypes;

public class S7TypeConverterTests
{
    [Theory]
    [InlineData(S7DataType.Bool, 1, true)]
    [InlineData(S7DataType.Bool, 0, false)]
    public void Serialize_Bool_RoundTrips(S7DataType type, byte wireValue, bool expected)
    {
        var bytes = S7TypeConverter.Serialize(type, expected);
        bytes[0].Should().Be(wireValue);
        S7TypeConverter.Deserialize(type, bytes).Should().Be(expected);
    }

    [Fact]
    public void Serialize_Int_BigEndian()
    {
        var bytes = S7TypeConverter.Serialize(S7DataType.Int, (short)256);
        bytes.Should().Equal(0x01, 0x00);
    }

    [Fact]
    public void Serialize_Real_RoundTrips()
    {
        float value = 3.14f;
        var bytes = S7TypeConverter.Serialize(S7DataType.Real, value);
        bytes.Length.Should().Be(4);
        var result = (float)S7TypeConverter.Deserialize(S7DataType.Real, bytes);
        result.Should().BeApproximately(value, 0.0001f);
    }

    [Fact]
    public void Serialize_S7String_RoundTrips()
    {
        var s7s = new S7String("PLC Test", 20);
        var bytes = S7TypeConverter.Serialize(S7DataType.S7String, s7s);
        var result = (S7String)S7TypeConverter.Deserialize(S7DataType.S7String, bytes);
        result.Value.Should().Be("PLC Test");
        result.MaxLength.Should().Be(20);
    }

    [Fact]
    public void Serialize_Raw_PassThrough()
    {
        byte[] raw = [1, 2, 3, 4, 5];
        var bytes = S7TypeConverter.Serialize(S7DataType.Raw, raw);
        bytes.Should().Equal(raw);
    }

    [Fact]
    public void Serialize_DateAndTime_RoundTrips()
    {
        var dt = new DateTime(2024, 6, 15, 12, 30, 45, 500);
        var bytes = S7TypeConverter.Serialize(S7DataType.DateAndTime, dt);
        bytes.Length.Should().Be(8);
        var result = (DateTime)S7TypeConverter.Deserialize(S7DataType.DateAndTime, bytes);
        result.Year.Should().Be(2024);
        result.Month.Should().Be(6);
        result.Day.Should().Be(15);
        result.Hour.Should().Be(12);
        result.Minute.Should().Be(30);
        result.Second.Should().Be(45);
        result.Millisecond.Should().Be(500);
    }
}
