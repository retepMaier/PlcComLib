using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Telegrams;
using Xunit;

namespace PlcComLib.Tests.Framing;

public class TelegramIdFramerTests
{
    // Build a framer with two known telegrams.
    // Definitions include the header sentinel fields as the source generator emits them:
    //   __MessageId   (Word = 2 bytes)
    //   __MessageLength (Word = 2 bytes)
    //   ... data fields
    // TotalWireSize = Fields.Sum(f => f.WireSize)

    // T1: 2 + 2 + 8 = 12 bytes   (one LWord data field)
    // T2: 2 + 2 + 2 = 6 bytes    (one Word data field)

    private static readonly TelegramDefinition T1 = new()
    {
        Id        = "T1",
        MessageId = 0x0001,
        Fields    =
        [
            new TelegramField { Name = "__TelegramId",     DataType = S7DataType.Word  }, // 2
            new TelegramField { Name = "__MessageLength", DataType = S7DataType.Word  }, // 2
            new TelegramField { Name = "f",               DataType = S7DataType.LWord }, // 8
        ],
    };

    private static readonly TelegramDefinition T2 = new()
    {
        Id        = "T2",
        MessageId = 0x0002,
        Fields    =
        [
            new TelegramField { Name = "__TelegramId",     DataType = S7DataType.Word }, // 2
            new TelegramField { Name = "__MessageLength", DataType = S7DataType.Word }, // 2
            new TelegramField { Name = "f",               DataType = S7DataType.Word }, // 2
        ],
    };

    private static TelegramIdFramer BuildFramer(ByteOrder order = ByteOrder.BigEndian) =>
        new([T1, T2], order);

    private static byte[] MakeFrame(ushort id, int totalSize, ByteOrder order = ByteOrder.BigEndian)
    {
        var buf = new byte[totalSize];
        if (order == ByteOrder.LittleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), id);
        else
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0), id);
        return buf;
    }

    [Fact]
    public void Frame_ReturnsPayloadAsIs()
    {
        var framer = BuildFramer();
        byte[] payload = [0x00, 0x01, 0x00, 0x0C, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88];
        framer.Frame(payload).Should().Equal(payload);
    }

    [Fact]
    public void TryExtract_ReturnsTrue_ForKnownTelegramId()
    {
        var framer = BuildFramer();
        var buffer = MakeFrame(0x0001, T1.TotalWireSize); // 12 bytes

        framer.TryExtract(buffer, out var message, out int consumed).Should().BeTrue();
        message.ToArray().Should().Equal(buffer);
        consumed.Should().Be(T1.TotalWireSize);
    }

    [Fact]
    public void TryExtract_ReturnsFalse_ForUnknownTelegramId()
    {
        var framer = BuildFramer();
        var buffer = MakeFrame(0x0099, 10);
        framer.TryExtract(buffer, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryExtract_ReturnsFalse_WhenBufferTooShortForId()
    {
        var framer = BuildFramer();
        framer.TryExtract(new byte[] { 0x00 }, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryExtract_ReturnsFalse_WhenPayloadIncomplete()
    {
        var framer = BuildFramer();
        // T1 needs 12 bytes, only provide 6
        var buffer = MakeFrame(0x0001, 6);
        framer.TryExtract(buffer, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void MultipleFrames_CanBeExtractedSequentially()
    {
        var framer = BuildFramer();

        var frame1 = MakeFrame(0x0001, T1.TotalWireSize); // 12 bytes
        var frame2 = MakeFrame(0x0002, T2.TotalWireSize); // 6 bytes
        var combined = frame1.Concat(frame2).ToArray();

        framer.TryExtract(combined, out var msg1, out int c1).Should().BeTrue();
        msg1.ToArray().Should().Equal(frame1);
        c1.Should().Be(T1.TotalWireSize);

        framer.TryExtract(combined.AsSpan(c1), out var msg2, out int c2).Should().BeTrue();
        msg2.ToArray().Should().Equal(frame2);
        c2.Should().Be(T2.TotalWireSize);
    }

    [Fact]
    public void LittleEndian_TryExtract_ReadsIdCorrectly()
    {
        var framer = BuildFramer(ByteOrder.LittleEndian);
        var buffer = MakeFrame(0x0001, T1.TotalWireSize, ByteOrder.LittleEndian);

        framer.TryExtract(buffer, out var message, out int consumed).Should().BeTrue();
        message.ToArray().Should().Equal(buffer);
        consumed.Should().Be(T1.TotalWireSize);
    }

    [Fact]
    public void ZeroMessageId_Definition_IsIgnored()
    {
        var definitions = new[]
        {
            new TelegramDefinition
            {
                Id        = "T0",
                MessageId = 0,
                Fields    = [new TelegramField { Name = "f", DataType = S7DataType.Word }],
            },
        };
        var framer = new TelegramIdFramer(definitions);
        var buffer = new byte[] { 0x00, 0x00, 0xAA };
        // MessageId=0 definitions are ignored; 0x0000 has no entry → false
        framer.TryExtract(buffer, out _, out _).Should().BeFalse();
    }
}

