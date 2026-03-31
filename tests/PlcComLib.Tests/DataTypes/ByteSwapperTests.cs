using FluentAssertions;
using PlcComLib.DataTypes;
using Xunit;

namespace PlcComLib.Tests.DataTypes;

public class ByteSwapperTests
{
    [Theory]
    [InlineData((ushort)0x1234, (ushort)0x3412)]
    [InlineData((ushort)0x0001, (ushort)0x0100)]
    [InlineData((ushort)0xABCD, (ushort)0xCDAB)]
    public void SwapUInt16_ReversesBytes(ushort input, ushort expected)
        => ByteSwapper.SwapUInt16(input).Should().Be(expected);

    [Theory]
    [InlineData((uint)0x12345678, (uint)0x78563412)]
    [InlineData((uint)0x00000001, (uint)0x01000000)]
    public void SwapUInt32_ReversesBytes(uint input, uint expected)
        => ByteSwapper.SwapUInt32(input).Should().Be(expected);

    [Theory]
    [InlineData(1.0f)]
    [InlineData(-1.0f)]
    [InlineData(3.14f)]
    [InlineData(float.MaxValue)]
    public void WriteAndReadReal_RoundTrips(float value)
    {
        var buf = new byte[4];
        ByteSwapper.WriteReal(buf, value);
        ByteSwapper.ReadReal(buf).Should().Be(value);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(3.141592653589793)]
    public void WriteAndReadLReal_RoundTrips(double value)
    {
        var buf = new byte[8];
        ByteSwapper.WriteLReal(buf, value);
        ByteSwapper.ReadLReal(buf).Should().Be(value);
    }

    [Fact]
    public void WriteAndRead_UInt16_BigEndian()
    {
        var buf = new byte[2];
        ByteSwapper.Write(buf, S7DataType.Word, (ushort)0x1234);
        buf.Should().Equal(0x12, 0x34);
        ByteSwapper.Read(buf, S7DataType.Word).Should().Be((ushort)0x1234);
    }

    [Fact]
    public void WriteAndRead_Int32_BigEndian()
    {
        var buf = new byte[4];
        ByteSwapper.Write(buf, S7DataType.DInt, -1);
        buf.Should().Equal(0xFF, 0xFF, 0xFF, 0xFF);
        ByteSwapper.Read(buf, S7DataType.DInt).Should().Be(-1);
    }

    [Fact]
    public void GetByteSize_ReturnsCorrectSizes()
    {
        ByteSwapper.GetByteSize(S7DataType.Bool).Should().Be(1);
        ByteSwapper.GetByteSize(S7DataType.Word).Should().Be(2);
        ByteSwapper.GetByteSize(S7DataType.DWord).Should().Be(4);
        ByteSwapper.GetByteSize(S7DataType.LWord).Should().Be(8);
        ByteSwapper.GetByteSize(S7DataType.Real).Should().Be(4);
        ByteSwapper.GetByteSize(S7DataType.LReal).Should().Be(8);
        ByteSwapper.GetByteSize(S7DataType.S7String).Should().Be(0);
    }
}
