using FluentAssertions;
using PlcComLib.Framing;
using Xunit;

namespace PlcComLib.Tests.Framing;

public class PayloadLengthFramerTests
{
    private readonly PayloadLengthFramer _framer = new();

    [Fact]
    public void Frame_PrependsBigEndianTotalLength()
    {
        byte[] payload = [0x01, 0x02, 0x03];
        var framed = _framer.Frame(payload);
        framed.Should().HaveCount(5);
        framed[0].Should().Be(0x00); framed[1].Should().Be(0x05);
        framed[2].Should().Be(0x01); framed[3].Should().Be(0x02); framed[4].Should().Be(0x03);
    }

    [Fact]
    public void TryExtract_ReturnsTrue_WhenCompleteMessage()
    {
        byte[] payload = [0xAA, 0xBB];
        var framed = _framer.Frame(payload);
        var result = _framer.TryExtract(framed, out var message, out int consumed);
        result.Should().BeTrue();
        message.ToArray().Should().Equal(0xAA, 0xBB);
        consumed.Should().Be(4);
    }

    [Fact]
    public void TryExtract_ReturnsFalse_WhenIncompleteHeader()
    {
        var partial = new byte[] { 0x00 };
        _framer.TryExtract(partial, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryExtract_ReturnsFalse_WhenPayloadIncomplete()
    {
        var buf = new byte[] { 0x00, 0x0C, 0x01, 0x02 };
        _framer.TryExtract(buf, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Frame_Then_TryExtract_RoundTrips()
    {
        byte[] payload = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        var framed = _framer.Frame(payload);
        _framer.TryExtract(framed, out var extracted, out int consumed).Should().BeTrue();
        extracted.ToArray().Should().Equal(payload);
        consumed.Should().Be(102);
    }

    [Fact]
    public void MultipleFrames_CanBeExtractedSequentially()
    {
        byte[] p1 = [1, 2, 3];
        byte[] p2 = [4, 5, 6, 7];
        var combined = _framer.Frame(p1).Concat(_framer.Frame(p2)).ToArray();

        _framer.TryExtract(combined, out var msg1, out int c1).Should().BeTrue();
        msg1.ToArray().Should().Equal(p1);

        _framer.TryExtract(combined.AsSpan(c1), out var msg2, out int c2).Should().BeTrue();
        msg2.ToArray().Should().Equal(p2);
    }
}
