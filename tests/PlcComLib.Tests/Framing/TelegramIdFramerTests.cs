using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Telegrams;
using Xunit;

namespace PlcComLib.Tests.Framing;

public class TelegramIdFramerTests
{
    // Build a framer with two hand-crafted telegram definitions.
    // Each definition manually includes a __TelegramId sentinel field so that
    // TotalWireSize (= Fields.Sum) covers the full frame size.
    // Source-generated definitions omit __TelegramId from the Fields list and
    // rely on ConfiguredWireSize instead.

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
        framer.TryExtract(buffer, out _, out int consumed).Should().BeFalse();
        // Buffer had enough bytes to check IDs — caller must discard 1 byte and scan forward.
        consumed.Should().Be(1);
    }

    [Fact]
    public void TryExtract_ReturnsFalse_WhenBufferTooShortForId()
    {
        var framer = BuildFramer();
        framer.TryExtract(new byte[] { 0x00 }, out _, out int consumed).Should().BeFalse();
        // Insufficient bytes to evaluate any ID — caller should wait for more data.
        consumed.Should().Be(0);
    }

    [Fact]
    public void TryExtract_ReturnsFalse_WhenPayloadIncomplete()
    {
        var framer = BuildFramer();
        // T1 needs 12 bytes; only 6 provided — ID matched but frame is incomplete.
        var buffer = MakeFrame(0x0001, 6);
        framer.TryExtract(buffer, out _, out int consumed).Should().BeFalse();
        // Caller must wait for more bytes (not discard).
        consumed.Should().Be(0);
    }

    [Fact]
    public void TryExtract_UnknownId_ConsumedAllowsBufferToAdvance()
    {
        // Simulate a receive buffer that starts with unrecognised bytes followed by a valid frame.
        // The caller (ProcessBuffer) should discard consumed bytes on each false return, eventually
        // reaching the valid frame.
        var framer = BuildFramer();

        var garbage = new byte[] { 0xFF, 0xFF }; // 2 bytes with unknown ID 0xFFFF
        var validFrame = MakeFrame(0x0001, T1.TotalWireSize); // 12 bytes, ID 0x0001
        var combined = garbage.Concat(validFrame).ToArray();

        // First call: unknown ID at head → consumed = 1, caller advances 1 byte.
        framer.TryExtract(combined, out _, out int c1).Should().BeFalse();
        c1.Should().Be(1);

        // Second call (after advancing 1 byte): still no match (0xFF .. 0x00 0x01 …)
        framer.TryExtract(combined.AsSpan(c1), out _, out int c2).Should().BeFalse();
        c2.Should().Be(1);

        // Third call (after advancing 2 bytes): now at the valid frame.
        framer.TryExtract(combined.AsSpan(c1 + c2), out var msg, out int c3).Should().BeTrue();
        c3.Should().Be(T1.TotalWireSize);
        msg.ToArray().Should().Equal(validFrame);
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
        // MessageId=0 definitions are ignored; registry is empty → no IDs checked → consumed = 0
        framer.TryExtract(buffer, out _, out int consumed).Should().BeFalse();
        consumed.Should().Be(0);
    }
}

