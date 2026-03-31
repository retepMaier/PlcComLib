using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.SourceGenerator;
using PlcComLib.Telegrams;

// File-scoped namespace must come first; the inline telegram classes live inside this namespace.
namespace PlcComLib.SourceGenerator.Tests;

// ── Inline typed telegram classes (source generator processes these at build time) ──

[S7Telegram(messageId: 0x0001)]
public partial class TestStatusTelegram
{
    [S7Word]              public ushort MachineId { get; set; }
    [S7Real]              public float  Speed     { get; set; }
    [S7String(maxLength: 10)] public string Label { get; set; } = "";
}

/// <summary>Covers every S7 scalar type.</summary>
[S7Telegram(messageId: 0x0002)]
public partial class FullTypeTelegram
{
    [S7Bool]  public bool   BoolVal  { get; set; }
    [S7Byte]  public byte   ByteVal  { get; set; }
    [S7SInt]  public sbyte  SIntVal  { get; set; }
    [S7Word]  public ushort WordVal  { get; set; }
    [S7Int]   public short  IntVal   { get; set; }
    [S7DWord] public uint   DWordVal { get; set; }
    [S7DInt]  public int    DIntVal  { get; set; }
    [S7Real]  public float  RealVal  { get; set; }
    [S7LWord] public ulong  LWordVal { get; set; }
    [S7LInt]  public long   LIntVal  { get; set; }
    [S7LReal] public double LRealVal { get; set; }
}

/// <summary>Little-endian telegram targeting a non-PLC device.</summary>
[S7Telegram(messageId: 0x0010, byteOrder: ByteOrder.LittleEndian)]
public partial class LittleEndianTelegram
{
    [S7Word] public ushort DeviceId { get; set; }
    [S7Real] public float  Value    { get; set; }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class TypedTelegramTests
{
    // ── 1. MessageId in first 2 bytes ─────────────────────────────────────────

    [Fact]
    public void Serialize_WritesMessageId_AsFirstTwoBytes()
    {
        var t = new TestStatusTelegram { MachineId = 7, Speed = 1.0f, Label = "hi" };
        var bytes = t.Serialize();

        ushort msgId = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        msgId.Should().Be(0x0001);
    }

    // ── 2. Round-trip ─────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ThenDeserialize_RoundTrips()
    {
        var original = new TestStatusTelegram { MachineId = 42, Speed = 123.456f, Label = "Hello" };
        var bytes    = original.Serialize();
        var restored = TestStatusTelegram.Deserialize(bytes);

        restored.MachineId.Should().Be(42);
        restored.Speed.Should().BeApproximately(123.456f, 1e-3f);
        restored.Label.Should().Be("Hello");
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

        restored.BoolVal .Should().Be(true);
        restored.ByteVal .Should().Be(0xFF);
        restored.SIntVal .Should().Be(-42);
        restored.WordVal .Should().Be(0xABCD);
        restored.IntVal  .Should().Be(-1000);
        restored.DWordVal.Should().Be(0xDEADBEEF);
        restored.DIntVal .Should().Be(-1_000_000);
        restored.RealVal .Should().BeApproximately(3.14f, 1e-4f);
        restored.LWordVal.Should().Be(0xCAFEBABEDEAD);
        restored.LIntVal .Should().Be(long.MinValue);
        restored.LRealVal.Should().BeApproximately(double.Pi, 1e-12);
    }

    // ── 3. Wrong MessageId throws ─────────────────────────────────────────────

    [Fact]
    public void Deserialize_ThrowsOnWrongMessageId()
    {
        var bytes = new TestStatusTelegram { MachineId = 1 }.Serialize();
        bytes[0] = 0x00;
        bytes[1] = 0x99;   // overwrite MessageId with 0x0099

        var act = () => TestStatusTelegram.Deserialize(bytes);
        act.Should().Throw<ArgumentException>().WithMessage("*MessageId mismatch*");
    }

    // ── 4. Short buffer throws ────────────────────────────────────────────────

    [Fact]
    public void Deserialize_ThrowsOnShortBuffer()
    {
        var act = () => TestStatusTelegram.Deserialize(new byte[3]);
        act.Should().Throw<ArgumentException>().WithMessage("*Buffer too short*");
    }

    // ── 5. WireSize includes 2-byte MessageId header ──────────────────────────

    [Fact]
    public void WireSize_IncludesMessageIdHeader()
    {
        // 2 (header) + 2 (Word) + 4 (Real) + 12 (S7String maxLen=10) = 20
        const int expected = 2 + 2 + 4 + (2 + 10);
        TestStatusTelegram.WireSize.Should().Be(expected);
    }

    [Fact]
    public void Serialize_ProducesExactlyWireSize_Bytes()
    {
        var bytes = new TestStatusTelegram().Serialize();
        bytes.Length.Should().Be(TestStatusTelegram.WireSize);
    }

    // ── 6. Definition first field is __MessageId ──────────────────────────────

    [Fact]
    public void Definition_FirstField_IsMessageId()
    {
        var def = TestStatusTelegram.Definition;
        def.Fields[0].Name.Should().Be("__MessageId");
        def.Fields[0].DataType.Should().Be(S7DataType.Word);
    }

    [Fact]
    public void Definition_MessageId_MatchesStaticProperty()
    {
        TestStatusTelegram.Definition.MessageId.Should().Be(TestStatusTelegram.MessageId);
    }

    // ── 7. Little-endian byte order ───────────────────────────────────────────

    [Fact]
    public void LittleEndian_Word_IsWrittenLittleEndian()
    {
        var t = new LittleEndianTelegram { DeviceId = 0x1234 };
        var bytes = t.Serialize();

        // bytes[0..1] = MessageId (always big-endian)
        // bytes[2..3] = DeviceId (little-endian)
        ushort leWord = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));
        leWord.Should().Be(0x1234);
    }

    [Fact]
    public void LittleEndian_RoundTrip()
    {
        var original = new LittleEndianTelegram { DeviceId = 0xBEEF, Value = 2.718f };
        var restored = LittleEndianTelegram.Deserialize(original.Serialize());

        restored.DeviceId.Should().Be(0xBEEF);
        restored.Value.Should().BeApproximately(2.718f, 1e-3f);
    }

    // ── 8. Definition usable via static interface member (zero reflection) ────

    [Fact]
    public void Definition_CanBeRegistered_WithoutReflection()
    {
        var registry = new TelegramRegistry();
        registry.Register(TestStatusTelegram.Definition);   // T.Definition — no reflection
        registry.TryGet("TestStatusTelegram", out var def).Should().BeTrue();
        def!.MessageId.Should().Be(0x0001);
    }
}
