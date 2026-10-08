using FluentAssertions;
using PlcComLib.Core.Internal;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Tcp;
using PlcComLib.Telegrams;

namespace PlcComLib.Tests.Regression;

/// <summary>Bugs 6 and 7: untyped decoding of typed layouts, and stream framing robustness.</summary>
public class FramingAndDecodingTests
{
    private static TelegramDefinition Def(string id, long messageId, int idOffset, int size) => new()
    {
        Id                  = id,
        MessageId           = messageId,
        MessageIdByteOffset = idOffset,
        MessageIdDataType   = S7DataType.Word,
        ConfiguredWireSize  = size,
    };

    // ── Bug 6: untyped decoder ───────────────────────────────────────────────

    [Fact]
    public void UntypedDeserialize_UsesPlcLayout_ForTypedDefinitions()
    {
        var bytes = new ReadmeMachineStatus { MachineId = 7, IsRunning = true, Temperature = 1.5f }.Serialize();

        var telegram = TelegramSerializer.Deserialize(ReadmeMachineStatus.Definition, bytes);

        telegram.GetValue<ushort>("MachineId").Should().Be(7);
        telegram.GetValue<bool>("IsRunning").Should().BeTrue();
        telegram.GetValue<float>("Temperature").Should().Be(1.5f);
    }

    [Fact]
    public void UntypedDeserialize_SupportsCharArray()
    {
        var bytes = new CharArrayStatus { TlgId = 3, Code = "AB".ToCharArray(), Ready = true, Value = 2.25f }.Serialize();

        var telegram = TelegramSerializer.Deserialize(CharArrayStatus.Definition, bytes);

        telegram.GetValue<string>("Code").Should().Be("AB");
        telegram.GetValue<bool>("Ready").Should().BeTrue();
        telegram.GetValue<float>("Value").Should().Be(2.25f);
    }

    [Fact]
    public void UntypedSerialize_ProducesSameBytesAsTypedSerializer()
    {
        var typed = new CharArrayStatus { TlgId = 3, Code = "XYZ".ToCharArray(), Ready = true, Value = -4f };
        var untyped = TelegramSerializer.Deserialize(CharArrayStatus.Definition, typed.Serialize());

        TelegramSerializer.Serialize(untyped).Should().Equal(typed.Serialize());
    }

    // ── Bug 7: framer ────────────────────────────────────────────────────────

    [Fact]
    public void PartialFrame_WithIdAtLargerOffset_IsNotDiscarded()
    {
        // A: id at offset 0; B: id at offset 4.
        var framer = new TelegramIdFramer([Def("A", 0x0A0A, 0, 6), Def("B", 0x0B0B, 4, 8)]);
        byte[] frameB = [0x01, 0x02, 0x03, 0x04, 0x0B, 0x0B, 0x07, 0x08];

        // Only 3 bytes have arrived: A can be checked (no match) but B cannot yet.
        framer.TryExtract(frameB.AsSpan(0, 3), out _, out int consumed).Should().BeFalse();
        consumed.Should().Be(0, "B's id has not arrived yet, so nothing may be discarded");

        framer.TryExtract(frameB, out var message, out consumed).Should().BeTrue();
        consumed.Should().Be(8);
        message.ToArray().Should().Equal(frameB);
    }

    [Fact]
    public void Junk_IsDiscarded_OnceEveryIdWasChecked()
    {
        var framer = new TelegramIdFramer([Def("A", 0x0A0A, 0, 4)]);
        framer.TryExtract(new byte[] { 0xFF, 0xFF }, out _, out int consumed).Should().BeFalse();
        consumed.Should().Be(1);
    }

    [Fact]
    public void DefinitionTooSmallForItsId_IsRejected()
    {
        var act = () => new TelegramIdFramer([new TelegramDefinition { Id = "Empty", MessageId = 1 }]);
        act.Should().Throw<ArgumentException>().WithMessage("*Empty*too small*");
    }

    [Fact]
    public void MixingIdAndSizeBasedDefinitions_IsRejected()
    {
        var sizeOnly = new TelegramDefinition { Id = "NoId", Fields = [new TelegramField { Name = "f", DataType = S7DataType.DWord }] };
        var act = () => new TelegramIdFramer([Def("A", 1, 0, 4), sizeOnly]);
        act.Should().Throw<ArgumentException>().WithMessage("*NoId*");
    }

    [Fact]
    public void SizeBasedDefinitionsWithDifferentSizes_AreRejected()
    {
        var two  = new TelegramDefinition { Id = "Two",  Fields = [new TelegramField { Name = "f", DataType = S7DataType.Word }] };
        var four = new TelegramDefinition { Id = "Four", Fields = [new TelegramField { Name = "f", DataType = S7DataType.DWord }] };
        var act = () => new TelegramIdFramer([two, four]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TcpBuilder_ReportsUnframeableRegistry_AtBuild()
    {
        var act = () => new TcpPlcClientBuilder()
            .RegisterTelegram(new TelegramDefinition { Id = "NoFields" })
                .WithMessageId<PlcComLib.Core.PlcTypes.S7Word>(1, 0)
            .Build();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EmptyRegistry_PassesDataThrough_InsteadOfBufferingForever()
    {
        var framer = new TelegramIdFramer([]);
        framer.TryExtract(new byte[] { 1, 2, 3 }, out var message, out int consumed).Should().BeTrue();
        consumed.Should().Be(3);
        message.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Accumulator_SkipsJunkInOnePass_AndKeepsFramesInOrder()
    {
        var framer      = new TelegramIdFramer([Def("A", 0x0A0A, 0, 4)]);
        var accumulator = new FrameAccumulator(framer, 1 << 20);
        var frames      = new List<byte[]>();

        var junk = Enumerable.Repeat((byte)0xEE, 100_000).ToArray();
        accumulator.Append(junk);
        accumulator.Append(new byte[] { 0x0A, 0x0A, 0x01, 0x02, 0x0A, 0x0A, 0x03 }); // one frame + partial

        accumulator.Drain(frames.Add).Should().Be(100_000);
        frames.Should().ContainSingle().Which.Should().Equal(0x0A, 0x0A, 0x01, 0x02);
        accumulator.Length.Should().Be(3, "the partial frame waits for more data");

        accumulator.Append(new byte[] { 0x04 });
        accumulator.Drain(frames.Add).Should().Be(0);
        frames.Should().HaveCount(2);
        frames[1].Should().Equal(0x0A, 0x0A, 0x03, 0x04);
        accumulator.Length.Should().Be(0);
    }

    [Fact]
    public void Accumulator_RejectsDataBeyondItsLimit()
    {
        var accumulator = new FrameAccumulator(new TelegramIdFramer([Def("A", 1, 0, 4)]), 16);
        var act = () => accumulator.Append(new byte[17]);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void HeaderWriter_RoundTripsThroughReadId()
    {
        var buffer = new byte[8];
        TelegramIdFramer.TryWriteInteger(buffer, 2, S7DataType.DInt, -5, ByteOrder.LittleEndian).Should().BeTrue();
        TelegramIdFramer.ReadId(buffer, 2, S7DataType.DInt, ByteOrder.LittleEndian).Should().Be(-5);

        TelegramIdFramer.TryWriteInteger(buffer, 7, S7DataType.Word, 1, ByteOrder.BigEndian)
            .Should().BeFalse("the field would extend past the buffer");
    }
}
