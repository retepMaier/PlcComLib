using FluentAssertions;
using PlcComLib.DataTypes;

namespace PlcComLib.Tests.DataTypes;

public class S7LayoutTests
{
    private static S7FieldDescriptor F(string name, S7DataType dt, int ws) => new(name, dt, ws);

    // ── 1. Single bool ────────────────────────────────────────────────────────

    [Fact]
    public void SingleBool_PlcOffset0_BitIndex0_TotalSize1()
    {
        var result = S7Layout.Compute([F("A", S7DataType.Bool, 1)]);
        result.Fields.Should().HaveCount(1);
        result.Fields[0].PlcOffset.Should().Be(0);
        result.Fields[0].BitIndex.Should().Be(0);
        result.TotalPlcSize.Should().Be(1);
    }

    // ── 2. Two consecutive bools share byte 0 ─────────────────────────────────

    [Fact]
    public void TwoConsecutiveBools_ShareByte0_TotalSize1()
    {
        var result = S7Layout.Compute(
        [
            F("A", S7DataType.Bool, 1),
            F("B", S7DataType.Bool, 1),
        ]);
        result.Fields[0].PlcOffset.Should().Be(0);
        result.Fields[0].BitIndex.Should().Be(0);
        result.Fields[1].PlcOffset.Should().Be(0);
        result.Fields[1].BitIndex.Should().Be(1);
        result.TotalPlcSize.Should().Be(1);
    }

    // ── 3. Eight bools — all in byte 0 ───────────────────────────────────────

    [Fact]
    public void EightBools_AllInByte0_TotalSize1()
    {
        var descriptors = Enumerable.Range(0, 8)
            .Select(i => F($"B{i}", S7DataType.Bool, 1))
            .ToList();
        var result = S7Layout.Compute(descriptors);
        result.TotalPlcSize.Should().Be(1);
        for (int i = 0; i < 8; i++)
        {
            result.Fields[i].PlcOffset.Should().Be(0, $"B{i} should share byte 0");
            result.Fields[i].BitIndex.Should().Be(i, $"B{i} should be at bit {i}");
        }
    }

    // ── 4. Nine bools — first 8 in byte 0, 9th in byte 1 ─────────────────────

    [Fact]
    public void NineBools_FirstEightInByte0_NinthInByte1()
    {
        var descriptors = Enumerable.Range(0, 9)
            .Select(i => F($"B{i}", S7DataType.Bool, 1))
            .ToList();
        var result = S7Layout.Compute(descriptors);
        result.TotalPlcSize.Should().Be(2);
        for (int i = 0; i < 8; i++)
            result.Fields[i].PlcOffset.Should().Be(0);
        result.Fields[8].PlcOffset.Should().Be(1);
        result.Fields[8].BitIndex.Should().Be(0);
    }

    // ── 5. Bool + Word — PaddingBefore=1, TotalPlcSize=4 ─────────────────────

    [Fact]
    public void BoolThenWord_PadsToEven_TotalSize4()
    {
        var result = S7Layout.Compute(
        [
            F("Flag",    S7DataType.Bool, 1),
            F("Counter", S7DataType.Word, 2),
        ]);
        result.Fields[0].PlcOffset.Should().Be(0);
        result.Fields[0].BitIndex.Should().Be(0);
        // Bool byte closed → offset=1 (odd) → pad 1 → Word at 2
        result.Fields[1].PlcOffset.Should().Be(2);
        result.Fields[1].PaddingBefore.Should().Be(1);
        result.TotalPlcSize.Should().Be(4);
        result.HasPadding.Should().BeTrue();
    }

    // ── 6. Two Bools + Word ───────────────────────────────────────────────────

    [Fact]
    public void TwoBoolsThenWord_BothBoolsInByte0_WordAt2_TotalSize4()
    {
        var result = S7Layout.Compute(
        [
            F("A",     S7DataType.Bool, 1),
            F("B",     S7DataType.Bool, 1),
            F("Value", S7DataType.Word, 2),
        ]);
        result.Fields[0].PlcOffset.Should().Be(0);
        result.Fields[1].PlcOffset.Should().Be(0);
        result.Fields[2].PlcOffset.Should().Be(2);
        result.Fields[2].PaddingBefore.Should().Be(1);
        result.TotalPlcSize.Should().Be(4);
        result.HasPadding.Should().BeTrue();
    }

    // ── 7. SInt + Word — SInt at 0, Word padded to 2 ─────────────────────────

    [Fact]
    public void SIntThenWord_OddOffset_PadsToEven_TotalSize4()
    {
        var result = S7Layout.Compute(
        [
            F("A", S7DataType.SInt, 1),
            F("B", S7DataType.Word, 2),
        ]);
        result.Fields[0].PlcOffset.Should().Be(0);
        result.Fields[1].PlcOffset.Should().Be(2);
        result.Fields[1].PaddingBefore.Should().Be(1);
        result.TotalPlcSize.Should().Be(4);
    }

    // ── 8. SInt + SInt + Word — no padding needed ─────────────────────────────

    [Fact]
    public void SIntSIntWord_NoAlignmentNeeded_HasPaddingFalse()
    {
        var result = S7Layout.Compute(
        [
            F("A", S7DataType.SInt, 1),
            F("B", S7DataType.SInt, 1),
            F("C", S7DataType.Word, 2),
        ]);
        result.Fields[0].PlcOffset.Should().Be(0);
        result.Fields[1].PlcOffset.Should().Be(1);
        result.Fields[2].PlcOffset.Should().Be(2);
        result.Fields[2].PaddingBefore.Should().Be(0);
        result.TotalPlcSize.Should().Be(4);
        result.HasPadding.Should().BeFalse();
    }

    // ── 9. Word + Bool + Word — bool byte at 2, pad to 4, Word at 4 ──────────

    [Fact]
    public void WordBoolWord_BoolByteAt2_SecondWordAt4_TotalSize6()
    {
        var result = S7Layout.Compute(
        [
            F("W1",   S7DataType.Word, 2),
            F("Flag", S7DataType.Bool, 1),
            F("W2",   S7DataType.Word, 2),
        ]);
        result.Fields[0].PlcOffset.Should().Be(0);
        result.Fields[1].PlcOffset.Should().Be(2);
        result.Fields[1].BitIndex.Should().Be(0);
        // Bool byte closed → offset=3 (odd) → pad 1 → Word at 4
        result.Fields[2].PlcOffset.Should().Be(4);
        result.Fields[2].PaddingBefore.Should().Be(1);
        result.TotalPlcSize.Should().Be(6);
    }

    // ── 10. All types — verify each type routes to the correct branch ──────────

    [Fact]
    public void AllTypesMixed_CorrectLayout()
    {
        // Bool: byte 0 bit 0, close → offset 1
        // Byte: 1-byte, PlcOffset=1, offset→2
        // Word: even(2), PlcOffset=2, offset→4
        // DInt: even(4), PlcOffset=4, offset→8
        // Real: even(8), PlcOffset=8, offset→12
        // LWord: even(12), PlcOffset=12, offset→20
        // Raw<4>: byte-aligned, PlcOffset=20, offset→24
        var result = S7Layout.Compute(
        [
            F("Flag",  S7DataType.Bool,  1),
            F("B",     S7DataType.Byte,  1),
            F("W",     S7DataType.Word,  2),
            F("DI",    S7DataType.DInt,  4),
            F("R",     S7DataType.Real,  4),
            F("LW",    S7DataType.LWord, 8),
            F("Raw",   S7DataType.Raw,   4),
        ]);

        result.Fields[0].PlcOffset.Should().Be(0);   // Bool
        result.Fields[0].BitIndex.Should().Be(0);
        result.Fields[1].PlcOffset.Should().Be(1);   // Byte (close bool byte → offset=1)
        result.Fields[2].PlcOffset.Should().Be(2);   // Word (offset=2, even)
        result.Fields[3].PlcOffset.Should().Be(4);   // DInt (offset=4, even)
        result.Fields[4].PlcOffset.Should().Be(8);   // Real (offset=8, even)
        result.Fields[5].PlcOffset.Should().Be(12);  // LWord (offset=12, even)
        result.Fields[6].PlcOffset.Should().Be(20);  // Raw (offset=20, byte-aligned)
        result.TotalPlcSize.Should().Be(24);
    }

    // ── TotalLibrarySize is always the sum of raw WireSizes ──────────────────

    [Fact]
    public void TotalLibrarySize_IsSumOfRawWireSizes()
    {
        var result = S7Layout.Compute(
        [
            F("A", S7DataType.Bool, 1),
            F("B", S7DataType.Word, 2),
        ]);
        result.TotalLibrarySize.Should().Be(3); // 1 + 2
    }

    // ── Non-Bool fields have BitIndex == -1 ──────────────────────────────────

    [Fact]
    public void NonBoolFields_BitIndex_IsMinusOne()
    {
        var result = S7Layout.Compute(
        [
            F("W", S7DataType.Word, 2),
            F("B", S7DataType.Byte, 1),
        ]);
        result.Fields[0].BitIndex.Should().Be(-1);
        result.Fields[1].BitIndex.Should().Be(-1);
    }
}
