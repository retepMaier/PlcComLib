using FluentAssertions;
using PlcComLib.DataTypes;
using Xunit;

namespace PlcComLib.Tests.DataTypes;

public class S7StringTests
{
    [Fact]
    public void WireSize_Is2PlusMaxLength()
    {
        var s = new S7String("Hello", maxLength: 10);
        s.WireSize.Should().Be(12);
    }

    [Fact]
    public void WriteTo_ProducesCorrectWireFormat()
    {
        var s = new S7String("Hi", maxLength: 5);
        var buf = new byte[s.WireSize];
        s.WriteTo(buf);
        buf[0].Should().Be(5);
        buf[1].Should().Be(2);
        buf[2].Should().Be((byte)'H');
        buf[3].Should().Be((byte)'i');
        buf[4].Should().Be(0);
        buf[5].Should().Be(0);
        buf[6].Should().Be(0);
    }

    [Fact]
    public void ReadFrom_ParsesWireFormat()
    {
        var original = new S7String("Test", maxLength: 20);
        var buf = new byte[original.WireSize];
        original.WriteTo(buf);

        var parsed = S7String.ReadFrom(buf);
        parsed.Value.Should().Be("Test");
        parsed.MaxLength.Should().Be(20);
    }

    [Fact]
    public void RoundTrip_PreservesValue()
    {
        var original = new S7String("Siemens S7!", maxLength: 40);
        var buf = new byte[original.WireSize];
        original.WriteTo(buf);
        var parsed = S7String.ReadFrom(buf);
        parsed.Value.Should().Be(original.Value);
        parsed.MaxLength.Should().Be(original.MaxLength);
    }

    [Fact]
    public void Constructor_ThrowsWhenValueExceedsMaxLength()
    {
        var act = () => new S7String("Too long", maxLength: 3);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ThrowsWhenMaxLengthExceeds254()
    {
        var act = () => new S7String("x", maxLength: 255);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReadFrom_ThrowsWhenActualLengthExceedsMaxLength()
    {
        byte[] corrupt = [10, 15, (byte)'x', 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var act = () => S7String.ReadFrom(corrupt);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void S7WString_RoundTrip()
    {
        var original = new S7WString("Hallo Welt", maxLength: 20);
        var buf = new byte[original.WireSize];
        original.WriteTo(buf);
        var parsed = S7WString.ReadFrom(buf);
        parsed.Value.Should().Be("Hallo Welt");
        parsed.MaxLength.Should().Be(20);
    }
}
