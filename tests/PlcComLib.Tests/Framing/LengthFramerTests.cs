using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Telegrams;
using Xunit;

namespace PlcComLib.Tests.Framing;

//public class LengthFramerTests
//{
//    private readonly LengthFramer _framer = new(ByteOrder.BigEndian);

//    // Wire format: [MsgId: 2][TotalLength: 2][data fields...]

//    [Fact]
//    public void Frame_ReturnsPayloadAsIs()
//    {
//        byte[] payload = [0x00, 0x01, 0x00, 0x06, 0xAA, 0xBB];
//        var framed = _framer.Frame(payload);
//        framed.Should().Equal(payload);
//    }

//    [Fact]
//    public void TryExtract_ReturnsTrue_WhenCompleteFrame()
//    {
//        // MsgId=0x0001, TotalLength=0x0006 (at bytes 2-3), then 2 data bytes
//        byte[] buffer = [0x00, 0x01, 0x00, 0x06, 0xAA, 0xBB];
//        var result = _framer.TryExtract(buffer, out var message, out int consumed);

//        result.Should().BeTrue();
//        message.ToArray().Should().Equal(buffer);
//        consumed.Should().Be(6);
//    }

//    [Fact]
//    public void TryExtract_ReturnsFalse_WhenBufferTooShortForHeader()
//    {
//        var partial = new byte[] { 0x00, 0x01, 0x00 }; // only 3 bytes
//        _framer.TryExtract(partial, out _, out _).Should().BeFalse();
//    }

//    [Fact]
//    public void TryExtract_ReturnsFalse_WhenPayloadIncomplete()
//    {
//        // TotalLength says 10 but only 6 bytes available
//        byte[] buffer = [0x00, 0x01, 0x00, 0x0A, 0xAA, 0xBB];
//        _framer.TryExtract(buffer, out _, out _).Should().BeFalse();
//    }

//    [Fact]
//    public void TryExtract_ReturnsFalse_WhenTotalLengthTooSmall()
//    {
//        // TotalLength=3 is below the 4-byte minimum
//        byte[] buffer = [0x00, 0x01, 0x00, 0x03];
//        _framer.TryExtract(buffer, out _, out _).Should().BeFalse();
//    }

//    [Fact]
//    public void Frame_Then_TryExtract_RoundTrips()
//    {
//        // Simulate a telegram whose first 4 bytes are [MsgId][TotalLength]
//        byte[] payload = new byte[12];
//        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0), 0x0001); // MsgId
//        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), 12);    // TotalLength
//        payload[4] = 0xAA; payload[5] = 0xBB; payload[6] = 0xCC;

//        var framed = _framer.Frame(payload);
//        _framer.TryExtract(framed, out var extracted, out int consumed).Should().BeTrue();
//        extracted.ToArray().Should().Equal(payload);
//        consumed.Should().Be(12);
//    }

//    [Fact]
//    public void MultipleFrames_CanBeExtractedSequentially()
//    {
//        byte[] frame1 = new byte[6];
//        BinaryPrimitives.WriteUInt16BigEndian(frame1.AsSpan(0), 0x0001);
//        BinaryPrimitives.WriteUInt16BigEndian(frame1.AsSpan(2), 6);
//        frame1[4] = 0x11; frame1[5] = 0x22;

//        byte[] frame2 = new byte[8];
//        BinaryPrimitives.WriteUInt16BigEndian(frame2.AsSpan(0), 0x0002);
//        BinaryPrimitives.WriteUInt16BigEndian(frame2.AsSpan(2), 8);
//        frame2[4] = 0x33; frame2[5] = 0x44; frame2[6] = 0x55; frame2[7] = 0x66;

//        var combined = frame1.Concat(frame2).ToArray();

//        _framer.TryExtract(combined, out var msg1, out int c1).Should().BeTrue();
//        msg1.ToArray().Should().Equal(frame1);
//        c1.Should().Be(6);

//        _framer.TryExtract(combined.AsSpan(c1), out var msg2, out int c2).Should().BeTrue();
//        msg2.ToArray().Should().Equal(frame2);
//        c2.Should().Be(8);
//    }

//    [Fact]
//    public void LittleEndian_TryExtract_ReadsTotalLengthCorrectly()
//    {
//        var leFramer = new LengthFramer(ByteOrder.LittleEndian);
//        byte[] buffer = new byte[8];
//        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0), 0x0001); // MsgId LE
//        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), 8);      // TotalLength LE
//        buffer[4] = 0xAA; buffer[5] = 0xBB; buffer[6] = 0xCC; buffer[7] = 0xDD;

//        leFramer.TryExtract(buffer, out var message, out int consumed).Should().BeTrue();
//        message.ToArray().Should().Equal(buffer);
//        consumed.Should().Be(8);
//    }
//}
