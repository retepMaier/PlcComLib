using FluentAssertions;
using PlcComLib.Core.PlcTypes;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.Tests.Telegrams;

/// <summary>
/// Telegram used for S7DataBlock tests.
/// MachineId (Word) at offset 0, Speed (Real) at offset 2, Label (S7String&lt;L10&gt;) at offset 6.
/// </summary>
public class DataBlockStatusTelegram : S7TelegramBase<DataBlockStatusTelegram>
{
    public S7Word        MachineId { get; set; } = 0;
    public S7Real        Speed     { get; set; } = 0f;
    public S7String<L10> Label     { get; set; } = "";
}

/// <summary>
/// Telegram with a Bool field for bit-packing tests.
/// BoolVal (Bool): byte 0 bit 0; next field ByteVal closes bool byte → offset 1.
/// ByteVal (Byte): offset 1.
/// WordVal (Word): offset 2 (even).
/// </summary>
public class DataBlockBoolTelegram : S7TelegramBase<DataBlockBoolTelegram>
{
    public S7Bool BoolVal { get; set; } = false;
    public S7Byte ByteVal { get; set; } = 0;
    public S7Word WordVal { get; set; } = 0;
}

public class S7DataBlockTests
{
    // ── 1. ToDataBlock length matches WireSize ────────────────────────────────

    [Fact]
    public void ToDataBlock_Length_MatchesWireSize()
    {
        var t     = new DataBlockStatusTelegram();
        var block = t.ToDataBlock();
        block.Length.Should().Be(DataBlockStatusTelegram.WireSize);
    }

    // ── 2. OffsetOf("MachineId") returns 0 ───────────────────────────────────

    [Fact]
    public void OffsetOf_MachineId_Returns0()
    {
        var block = new DataBlockStatusTelegram().ToDataBlock();
        block.OffsetOf("MachineId").Should().Be(0);
    }

    // ── 3. OffsetOf("Speed") returns 2 ───────────────────────────────────────

    [Fact]
    public void OffsetOf_Speed_Returns2()
    {
        var block = new DataBlockStatusTelegram().ToDataBlock();
        block.OffsetOf("Speed").Should().Be(2);
    }

    // ── 4. Indexer returns correct 2-byte span for MachineId ─────────────────

    [Fact]
    public void Indexer_MachineId_ReturnsCorrect2ByteSpan()
    {
        var t     = new DataBlockStatusTelegram { MachineId = 0x1234 };
        var block = t.ToDataBlock();
        var span  = block["MachineId"].ToArray();
        span.Should().HaveCount(2);
        // BigEndian: 0x12, 0x34
        span[0].Should().Be(0x12);
        span[1].Should().Be(0x34);
    }

    // ── 5. ToArray() returns same bytes as Serialize() ────────────────────────

    [Fact]
    public void ToArray_ReturnsSameBytesAsSerialize()
    {
        var t       = new DataBlockStatusTelegram { MachineId = 99, Speed = 1.5f, Label = "hi" };
        var block   = t.ToDataBlock();
        var fromSer = t.Serialize();
        block.ToArray().Should().Equal(fromSer);
    }

    // ── 6. ToString() returns non-empty hex string ────────────────────────────

    [Fact]
    public void ToString_ReturnsNonEmptyHexString()
    {
        var block = new DataBlockStatusTelegram().ToDataBlock();
        block.ToString().Should().NotBeNullOrEmpty();
        // Format: "XX XX XX …"
        block.ToString().Should().MatchRegex(@"^([0-9A-F]{2}( [0-9A-F]{2})*)?$");
    }

    // ── 7. Bool field indexer returns 1-byte span ─────────────────────────────

    [Fact]
    public void Indexer_BoolField_Returns1ByteSpan()
    {
        var t     = new DataBlockBoolTelegram { BoolVal = true };
        var block = t.ToDataBlock();
        var span  = block["BoolVal"].ToArray();
        span.Should().HaveCount(1);
    }

    // ── 8. ToDataBlock round-trips with Deserialize ───────────────────────────

    [Fact]
    public void ToDataBlock_RoundTrips_WithDeserialize()
    {
        var original = new DataBlockStatusTelegram { MachineId = 42, Speed = 3.14f, Label = "Test" };
        var block    = original.ToDataBlock();
        var restored = DataBlockStatusTelegram.Deserialize(block.ToArray());

        ((ushort)restored.MachineId).Should().Be(42);
        ((float) restored.Speed    ).Should().BeApproximately(3.14f, 1e-4f);
        ((string)restored.Label   ).Should().Be("Test");
    }

    // ── 9. Unknown field name throws KeyNotFoundException ─────────────────────

    [Fact]
    public void Indexer_UnknownField_Throws()
    {
        var block = new DataBlockStatusTelegram().ToDataBlock();
        var act   = () => _ = block["NonExistent"].ToArray();
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void OffsetOf_UnknownField_Throws()
    {
        var block = new DataBlockStatusTelegram().ToDataBlock();
        var act   = () => block.OffsetOf("NonExistent");
        act.Should().Throw<KeyNotFoundException>();
    }

    // ── 10. Fields property returns all fields ────────────────────────────────

    [Fact]
    public void Fields_ReturnsAllFields()
    {
        var block = new DataBlockStatusTelegram().ToDataBlock();
        block.Fields.Should().HaveCount(3);
        block.Fields[0].Name.Should().Be("MachineId");
        block.Fields[1].Name.Should().Be("Speed");
        block.Fields[2].Name.Should().Be("Label");
    }

    // ── 11. Bool bit-packing — BoolVal at byte 0 bit 0 ───────────────────────

    [Fact]
    public void BoolField_PlcOffset_IsCorrect()
    {
        var block = new DataBlockBoolTelegram().ToDataBlock();
        block.OffsetOf("BoolVal").Should().Be(0);
        block.OffsetOf("ByteVal").Should().Be(1);
        block.OffsetOf("WordVal").Should().Be(2);
    }

    [Fact]
    public void BoolField_SerializeDeserialize_RoundTrip()
    {
        var original = new DataBlockBoolTelegram { BoolVal = true, ByteVal = 0xAB, WordVal = 0x1234 };
        var block    = original.ToDataBlock();
        var restored = DataBlockBoolTelegram.Deserialize(block.ToArray());

        ((bool)  restored.BoolVal).Should().Be(true);
        ((byte)  restored.ByteVal).Should().Be(0xAB);
        ((ushort)restored.WordVal).Should().Be(0x1234);
    }
}
