using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.SourceGenerator;
using Xunit;

namespace PlcComLib.SourceGenerator.Tests;

/// <summary>
/// Tests specifically validating that [MsgLength] is serialized into the wire bytes
/// and that the Deserialize path validates the embedded length.
/// </summary>
public class MsgLengthSerializationTests
{
    // ── Wire layout tests ─────────────────────────────────────────────────────

    [Fact]
    public void Serialize_WritesMsgLength_AtBytes2To3_BigEndian()
    {
        // Wire: [MsgId:2][MsgLength:2][fields...]
        var t = new TestStatusTelegram { MachineId = 1, Speed = 0f, Label = "" };
        var bytes = t.Serialize(ByteOrder.BigEndian);

        ushort embeddedLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2));
        embeddedLength.Should().Be((ushort)TestStatusTelegram.WireSize);
    }

    [Fact]
    public void Serialize_WritesMsgLength_AtBytes2To3_LittleEndian()
    {
        var t = new TestStatusTelegram { MachineId = 1, Speed = 0f, Label = "" };
        var bytes = t.Serialize(ByteOrder.LittleEndian);

        ushort embeddedLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));
        embeddedLength.Should().Be((ushort)TestStatusTelegram.WireSize);
    }

    [Fact]
    public void Serialize_MsgLengthValue_EqualsTotalWireSize_IncludingItself()
    {
        // MsgLength is at bytes 2-3 and its value includes all bytes (MsgId + MsgLength + fields)
        var t = new TestStatusTelegram();
        var bytes = t.Serialize();

        ushort msgLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2));
        msgLength.Should().Be((ushort)bytes.Length,
            because: "the embedded MsgLength must account for the full buffer including itself");
    }

    [Fact]
    public void Serialize_DataFields_StartAtOffset4_WhenMsgLengthPresent()
    {
        // Layout: [MsgId:2][MsgLength:2][MachineId:2][Speed:4][Label:12]
        var t = new TestStatusTelegram { MachineId = 0xABCD };
        var bytes = t.Serialize(ByteOrder.BigEndian);

        ushort machineId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        machineId.Should().Be(0xABCD);
    }

    [Fact]
    public void Serialize_MsgId_StillAtOffset0()
    {
        var t = new TestStatusTelegram();
        var bytes = t.Serialize();

        ushort msgId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0));
        msgId.Should().Be(TestStatusTelegram.MessageId);
    }

    // ── Deserialize validation tests ──────────────────────────────────────────

    [Fact]
    public void Deserialize_ThrowsOnMsgLengthMismatch()
    {
        var bytes = new TestStatusTelegram { MachineId = 1 }.Serialize();
        // Corrupt the embedded MsgLength at bytes 2-3
        bytes[2] = 0x00;
        bytes[3] = 0x05; // wrong length

        var act = () => TestStatusTelegram.Deserialize(bytes);
        act.Should().Throw<ArgumentException>().WithMessage("*MessageLength mismatch*");
    }

    [Fact]
    public void Deserialize_SucceedsWithCorrectEmbeddedLength()
    {
        var original = new TestStatusTelegram { MachineId = 99, Speed = 1.5f, Label = "ok" };
        var bytes = original.Serialize();
        var restored = TestStatusTelegram.Deserialize(bytes);

        restored.MachineId.Should().Be(99);
        restored.Speed.Should().BeApproximately(1.5f, 1e-4f);
        restored.Label.Should().Be("ok");
    }

    // ── Definition fields include __MessageLength ─────────────────────────────

    [Fact]
    public void Definition_SecondField_IsMessageLength()
    {
        var def = TestStatusTelegram.Definition;
        def.Fields[1].Name.Should().Be("__MessageLength");
        def.Fields[1].DataType.Should().Be(S7DataType.Word);
    }

    [Fact]
    public void Definition_TotalWireSize_IncludesMsgLengthField()
    {
        // TotalWireSize is computed from Fields.Sum(f => f.WireSize)
        // Should include: __MessageId(2) + __MessageLength(2) + data fields
        TestStatusTelegram.Definition.TotalWireSize.Should().Be(TestStatusTelegram.WireSize);
    }
}
