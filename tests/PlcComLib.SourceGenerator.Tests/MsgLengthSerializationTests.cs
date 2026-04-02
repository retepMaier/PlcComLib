using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;
using Xunit;

namespace PlcComLib.SourceGenerator.Tests;

/// <summary>
/// Validates wire format and builder-style MessageId/WireSize configuration
/// using the new S7TelegramBase&lt;T&gt; approach.
/// </summary>
public class WireFormatTests
{
    // ── Wire layout ────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_DataFields_StartAtOffset0()
    {
        var t     = new TestStatusTelegram { MachineId = 0xABCD };
        var bytes = t.Serialize(ByteOrder.BigEndian);
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0)).Should().Be(0xABCD);
    }

    [Fact]
    public void Serialize_TotalSize_IsSumOfDataFields()
    {
        // S7Word(2) + S7Real(4) + S7String<L10>(12) = 18
        new TestStatusTelegram().Serialize().Length.Should().Be(18);
    }

    // ── Deserialize ────────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_Succeeds_RoundTrip()
    {
        var original = new TestStatusTelegram { MachineId = 99, Speed = 1.5f, Label = "ok" };
        var bytes    = original.Serialize();
        var restored = TestStatusTelegram.Deserialize(bytes);

        ((ushort)restored.MachineId).Should().Be(99);
        ((float) restored.Speed    ).Should().BeApproximately(1.5f, 1e-4f);
        ((string)restored.Label   ).Should().Be("ok");
    }

    // ── Definition structure ───────────────────────────────────────────────────

    [Fact]
    public void Definition_FirstField_IsFirstDataField()
    {
        var def = TestStatusTelegram.Definition;
        def.Fields[0].Name.Should().Be("MachineId");
        def.Fields[0].DataType.Should().Be(S7DataType.Word);
    }

    [Fact]
    public void Definition_ConfiguredWireSize_MatchesWireSize()
    {
        TestStatusTelegram.Definition.ConfiguredWireSize
            .Should().Be(TestStatusTelegram.WireSize);
    }

    [Fact]
    public void Definition_EffectiveWireSize_MatchesWireSize()
    {
        TestStatusTelegram.Definition.EffectiveWireSize
            .Should().Be(TestStatusTelegram.WireSize);
    }

    // ── Builder-style MessageId simulation ────────────────────────────────────

    [Fact]
    public void WithMessageId_Sets_DefinitionMessageId()
    {
        var saved = TestStatusTelegram.Definition.MessageId;
        try
        {
            TestStatusTelegram.Definition.MessageId         = 0x0001;
            TestStatusTelegram.Definition.ConfiguredWireSize = TestStatusTelegram.WireSize;

            TestStatusTelegram.Definition.MessageId.Should().Be(0x0001);
            TestStatusTelegram.Definition.ConfiguredWireSize.Should().Be(TestStatusTelegram.WireSize);
        }
        finally
        {
            TestStatusTelegram.Definition.MessageId         = saved;
            TestStatusTelegram.Definition.ConfiguredWireSize = TestStatusTelegram.WireSize;
        }
    }

    [Fact]
    public void TelegramDefinition_EffectiveWireSize_FallsBackToTotalWireSize_WhenConfiguredIsZero()
    {
        var def = new TelegramDefinition
        {
            Id                 = "TestHand2",
            MessageId          = 0x00FE,
            ConfiguredWireSize = 0,
            Fields =
            [
                new TelegramField { Name = "A", DataType = S7DataType.Word },
                new TelegramField { Name = "B", DataType = S7DataType.Real },
            ],
        };
        def.EffectiveWireSize.Should().Be(2 + 4);
    }
}
