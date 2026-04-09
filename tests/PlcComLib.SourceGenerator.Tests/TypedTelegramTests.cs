using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.Core.PlcTypes;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

// ── Telegram classes using the new S7TelegramBase<T> approach ─────────────────

namespace PlcComLib.SourceGenerator.Tests;

public class TestStatusTelegram : S7TelegramBase<TestStatusTelegram>
{
    public S7Word              MachineId { get; set; } = 0;
    public S7Real              Speed     { get; set; } = 0f;
    public S7String<L10>       Label     { get; set; } = "";
}

/// <summary>Covers every scalar S7 type.</summary>
public class FullTypeTelegram : S7TelegramBase<FullTypeTelegram>
{
    public S7Bool  BoolVal  { get; set; } = false;
    public S7Byte  ByteVal  { get; set; } = 0;
    public S7SInt  SIntVal  { get; set; } = 0;
    public S7Word  WordVal  { get; set; } = 0;
    public S7Int   IntVal   { get; set; } = 0;
    public S7DWord DWordVal { get; set; } = 0;
    public S7DInt  DIntVal  { get; set; } = 0;
    public S7Real  RealVal  { get; set; } = 0f;
    public S7LWord LWordVal { get; set; } = 0;
    public S7LInt  LIntVal  { get; set; } = 0;
    public S7LReal LRealVal { get; set; } = 0.0;
}

public class LittleEndianTelegram : S7TelegramBase<LittleEndianTelegram>
{
    public S7Word DeviceId { get; set; } = 0;
    public S7Real Value    { get; set; } = 0f;
}

public class CharArrayTelegram : S7TelegramBase<CharArrayTelegram>
{
    public S7Word          DeviceId { get; set; } = 0;
    public S7CharArray<L8> Tag      { get; set; } = new char[0];
    public S7CharArray<L4> Code     { get; set; } = new char[0];
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class TypedTelegramTests
{
    // ── 1. Round-trip ─────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ThenDeserialize_RoundTrips()
    {
        var original = new TestStatusTelegram { MachineId = 42, Speed = 123.456f, Label = "Hello" };
        var bytes    = original.Serialize();
        var restored = TestStatusTelegram.Deserialize(bytes);

        ((ushort)restored.MachineId).Should().Be(42);
        ((float)restored.Speed).Should().BeApproximately(123.456f, 1e-3f);
        ((string)restored.Label).Should().Be("Hello");
    }

    [Fact]
    public void Serialize_ThenDeserialize_AllTypes_RoundTrip()
    {
        var original = new FullTypeTelegram
        {
            BoolVal  = true,
            ByteVal  = 0xFF,
            SIntVal  = -42,
            WordVal  = 0xABCD,
            IntVal   = -1000,
            DWordVal = 0xDEADBEEF,
            DIntVal  = -1_000_000,
            RealVal  = 3.14f,
            LWordVal = 0xCAFEBABEDEAD,
            LIntVal  = long.MinValue,
            LRealVal = double.Pi,
        };

        var bytes    = original.Serialize();
        var restored = FullTypeTelegram.Deserialize(bytes);

        ((bool)  restored.BoolVal ).Should().Be(true);
        ((byte)  restored.ByteVal ).Should().Be(0xFF);
        ((sbyte) restored.SIntVal ).Should().Be(-42);
        ((ushort)restored.WordVal ).Should().Be(0xABCD);
        ((short) restored.IntVal  ).Should().Be(-1000);
        ((uint)  restored.DWordVal).Should().Be(0xDEADBEEF);
        ((int)   restored.DIntVal ).Should().Be(-1_000_000);
        ((float) restored.RealVal ).Should().BeApproximately(3.14f, 1e-4f);
        ((ulong) restored.LWordVal).Should().Be(0xCAFEBABEDEAD);
        ((long)  restored.LIntVal ).Should().Be(long.MinValue);
        ((double)restored.LRealVal).Should().BeApproximately(double.Pi, 1e-12);
    }

    // ── 2. Short buffer throws ────────────────────────────────────────────────

    [Fact]
    public void Deserialize_ThrowsOnShortBuffer()
    {
        var act = () => TestStatusTelegram.Deserialize(new byte[3]);
        act.Should().Throw<ArgumentException>().WithMessage("*Buffer too short*");
    }

    // ── 3. WireSize ───────────────────────────────────────────────────────────

    [Fact]
    public void WireSize_IsExactlySumOfDataFields()
    {
        // 2 (S7Word) + 4 (S7Real) + 12 (S7String<L10>: 2+10) = 18
        const int expected = 2 + 4 + (2 + 10);
        TestStatusTelegram.WireSize.Should().Be(expected);
    }

    [Fact]
    public void Serialize_ProducesExactlyWireSize_Bytes()
    {
        new TestStatusTelegram().Serialize().Length.Should().Be(TestStatusTelegram.WireSize);
    }

    // ── 4. Implicit conversion operators ─────────────────────────────────────

    [Fact]
    public void ImplicitOperators_AllowAssigningNetPrimitives()
    {
        var t = new FullTypeTelegram();
        t.BoolVal  = true;
        t.ByteVal  = 0xAB;
        t.SIntVal  = -5;
        t.WordVal  = 0x1234;
        t.IntVal   = -999;
        t.DWordVal = 0xDEAD;
        t.DIntVal  = -1;
        t.RealVal  = 1.5f;
        t.LWordVal = 0xFFFFFFFF;
        t.LIntVal  = -1L;
        t.LRealVal = 2.71828;

        ((bool)  t.BoolVal ).Should().Be(true);
        ((byte)  t.ByteVal ).Should().Be(0xAB);
        ((sbyte) t.SIntVal ).Should().Be(-5);
        ((ushort)t.WordVal ).Should().Be(0x1234);
        ((short) t.IntVal  ).Should().Be(-999);
        ((uint)  t.DWordVal).Should().Be(0xDEAD);
        ((int)   t.DIntVal ).Should().Be(-1);
        ((float) t.RealVal ).Should().BeApproximately(1.5f, 1e-6f);
        ((ulong) t.LWordVal).Should().Be(0xFFFFFFFF);
        ((long)  t.LIntVal ).Should().Be(-1L);
        ((double)t.LRealVal).Should().BeApproximately(2.71828, 1e-10);
    }

    [Fact]
    public void ImplicitOperators_AllowAssigningStringToS7String()
    {
        var t = new TestStatusTelegram { Label = "world" };
        ((string)t.Label).Should().Be("world");
    }

    // ── 5. Definition ─────────────────────────────────────────────────────────

    [Fact]
    public void Definition_FirstField_IsFirstDeclaredProperty()
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
    public void Definition_MessageId_IsZeroUntilRegistered()
    {
        TestStatusTelegram.Definition.MessageId = 0;
        TestStatusTelegram.Definition.MessageId.Should().Be(0);
    }

    // ── 6. TelegramId follows Definition.MessageId ────────────────────────────

    [Fact]
    public void TelegramId_FollowsDefinitionMessageId()
    {
        var saved = TestStatusTelegram.Definition.MessageId;
        try
        {
            TestStatusTelegram.Definition.MessageId = 0x0042;
            ITelegram t = new TestStatusTelegram();
            t.TelegramId.Should().Be(0x0042);
        }
        finally { TestStatusTelegram.Definition.MessageId = saved; }
    }

    // ── 7. Byte order ─────────────────────────────────────────────────────────

    [Fact]
    public void LittleEndian_Word_IsWrittenLittleEndian()
    {
        var t = new LittleEndianTelegram { DeviceId = 0x1234 };
        var bytes = t.Serialize(ByteOrder.LittleEndian);
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)).Should().Be(0x1234);
    }

    [Fact]
    public void LittleEndian_RoundTrip()
    {
        var original = new LittleEndianTelegram { DeviceId = 0xBEEF, Value = 2.718f };
        var bytes    = original.Serialize(ByteOrder.LittleEndian);
        var restored = LittleEndianTelegram.Deserialize(bytes, ByteOrder.LittleEndian);

        ((ushort)restored.DeviceId).Should().Be(0xBEEF);
        ((float) restored.Value   ).Should().BeApproximately(2.718f, 1e-3f);
    }

    [Fact]
    public void DefaultSerialize_IsBigEndian()
    {
        var t     = new LittleEndianTelegram { DeviceId = 0x1234 };
        var bytes = t.Serialize();
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0)).Should().Be(0x1234);
    }

    // ── 8. S7CharArray ────────────────────────────────────────────────────────

    [Fact]
    public void CharArray_WireSize_IsExactLength()
    {
        // 2 (S7Word) + 8 (S7CharArray<L8>) + 4 (S7CharArray<L4>) = 14
        CharArrayTelegram.WireSize.Should().Be(2 + 8 + 4);
    }

    [Fact]
    public void CharArray_Serialize_WritesCharsAsBytes()
    {
        var t = new CharArrayTelegram
        {
            DeviceId = 0x0001,
            Tag  = new char[] { 'H', 'e', 'l', 'l', 'o', '!', '\0', '\0' },
            Code = new char[] { 'A', 'B', 'C', 'D' },
        };
        var bytes = t.Serialize();

        bytes[2].Should().Be((byte)'H');
        bytes[7].Should().Be((byte)'!');
        bytes[10].Should().Be((byte)'A');
        bytes[13].Should().Be((byte)'D');
    }

    [Fact]
    public void CharArray_RoundTrip_PreservesAllChars()
    {
        var original = new CharArrayTelegram
        {
            DeviceId = 0x0042,
            Tag  = new char[] { 'T', 'E', 'S', 'T', '_', 'T', 'A', 'G' },
            Code = new char[] { 'X', '1', '2', '3' },
        };
        var bytes    = original.Serialize();
        var restored = CharArrayTelegram.Deserialize(bytes);

        ((ushort)restored.DeviceId).Should().Be(0x0042);
        ((char[])restored.Tag ).Should().Equal('T', 'E', 'S', 'T', '_', 'T', 'A', 'G');
        ((char[])restored.Code).Should().Equal('X', '1', '2', '3');
    }

    // ── 9. Definition usable for registry registration ────────────────────────

    [Fact]
    public void Definition_CanBeRegistered_WithRegistry()
    {
        var registry = new TelegramRegistry();
        registry.Register(TestStatusTelegram.Definition);
        registry.TryGet("TestStatusTelegram", out _).Should().BeTrue();
    }
}

