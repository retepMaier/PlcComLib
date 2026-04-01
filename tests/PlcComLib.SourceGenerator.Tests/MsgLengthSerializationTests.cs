using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.SourceGenerator;
using PlcComLib.Telegrams;
using Xunit;

namespace PlcComLib.SourceGenerator.Tests;

/// <summary>
/// Validates the new wire format: [TelegramId: 2 bytes][data fields…].
/// MessageId and wire-size are registered at runtime via the connection builder's
/// .WithMessageId() / .WithLength() — not via [MsgId] / [MsgLength] attributes.
/// </summary>
public class WireFormatTests
{
    // ── Wire layout ────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_DataFields_StartAtOffset0()
    {
        // Wire layout: [MachineId: 2][Speed: 4][Label: 12] — no prepended header.
        var t = new TestStatusTelegram { MachineId = 0xABCD };
        var bytes = t.Serialize(ByteOrder.BigEndian);

        ushort machineId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0));
        machineId.Should().Be(0xABCD);
    }

    [Fact]
    public void Serialize_TotalSize_IsSumOfDataFields()
    {
        // 2 (Word) + 4 (Real) + 12 (S7String maxLen=10) = 18
        var bytes = new TestStatusTelegram().Serialize();
        bytes.Length.Should().Be(18);
    }

    // ── Deserialize ────────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_Succeeds_RoundTrip()
    {
        var original = new TestStatusTelegram { MachineId = 99, Speed = 1.5f, Label = "ok" };
        var bytes    = original.Serialize();
        var restored = TestStatusTelegram.Deserialize(bytes);

        restored.MachineId.Should().Be(99);
        restored.Speed.Should().BeApproximately(1.5f, 1e-4f);
        restored.Label.Should().Be("ok");
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
    public void Definition_HasNoSentinelHeaderFields()
    {
        // The generator adds no __TelegramId or __MessageLength sentinel fields.
        var def = TestStatusTelegram.Definition;
        def.Fields.Should().NotContain(f => f.Name == "__TelegramId");
        def.Fields.Should().NotContain(f => f.Name == "__MessageLength");
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

    // ── Builder-style .WithMessageId / .WithLength simulation ─────────────────

    [Fact]
    public void WithMessageId_Sets_DefinitionMessageId()
    {
        var saved = TestStatusTelegram.Definition.MessageId;
        try
        {
            // Simulate what the connection builder does:
            // .RegisterTelegram<TestStatusTelegram>().WithMessageId(0x0001).WithLength(WireSize)
            TestStatusTelegram.Definition.MessageId        = 0x0001;
            TestStatusTelegram.Definition.ConfiguredWireSize = TestStatusTelegram.WireSize;

            TestStatusTelegram.Definition.MessageId.Should().Be(0x0001);
            TestStatusTelegram.Definition.ConfiguredWireSize.Should().Be(TestStatusTelegram.WireSize);
        }
        finally
        {
            TestStatusTelegram.Definition.MessageId        = saved;
            TestStatusTelegram.Definition.ConfiguredWireSize = TestStatusTelegram.WireSize;
        }
    }

    [Fact]
    public void TelegramIdFramer_UsesEffectiveWireSize_FromDefinition()
    {
        // EffectiveWireSize is ConfiguredWireSize when set, else TotalWireSize from fields.
        var def = new TelegramDefinition
        {
            Id = "TestHand",
            MessageId = 0x00FF,
            ConfiguredWireSize = 42,
            Fields = [],
        };
        def.EffectiveWireSize.Should().Be(42);
    }

    [Fact]
    public void TelegramDefinition_EffectiveWireSize_FallsBackToTotalWireSize_WhenConfiguredIsZero()
    {
        var def = new TelegramDefinition
        {
            Id = "TestHand2",
            MessageId = 0x00FE,
            ConfiguredWireSize = 0,
            Fields =
            [
                new TelegramField { Name = "A", DataType = S7DataType.Word },
                new TelegramField { Name = "B", DataType = S7DataType.Real },
            ],
        };
        def.EffectiveWireSize.Should().Be(2 + 4); // Word + Real
    }
}
