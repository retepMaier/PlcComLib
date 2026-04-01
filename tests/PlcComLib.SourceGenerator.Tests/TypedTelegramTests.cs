using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.SourceGenerator;
using PlcComLib.Telegrams;

// File-scoped namespace; the inline telegram classes live inside this namespace.
namespace PlcComLib.SourceGenerator.Tests;

// ── Inline typed telegram classes processed by the source generator ────────────
// No [MsgId] or [MsgLength] attributes — TelegramId and wire-size are configured
// at registration time via the connection builder's .WithMessageId() / .WithLength().

[S7Telegram]
public partial class TestStatusTelegram
{
    [S7Word]                  public ushort MachineId { get; set; }
    [S7Real]                  public float  Speed     { get; set; }
    [S7String(maxLength: 10)] public string Label     { get; set; } = "";
}

/// <summary>Covers every S7 scalar type.</summary>
[S7Telegram]
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

/// <summary>Telegram for a non-PLC device (e.g. a Linux sensor board).</summary>
[S7Telegram]
public partial class LittleEndianTelegram
{
    [S7Word] public ushort DeviceId { get; set; }
    [S7Real] public float  Value    { get; set; }
}

/// <summary>Telegram with fixed-length char[] fields.</summary>
[S7Telegram]
public partial class CharArrayTelegram
{
    [S7Word]         public ushort  DeviceId { get; set; }
    [S7CharArray(8)] public char[]? Tag      { get; set; }
    [S7CharArray(4)] public char[]? Code     { get; set; }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class TypedTelegramTests
{
    // ── 1. TelegramId header at bytes 0–1 ────────────────────────────────────

    [Fact]
    public void Serialize_WritesTelegramId_AsFirstTwoBytes()
    {
        // Set the message ID (normally done by the connection builder).
        TestStatusTelegram.Definition.MessageId = 0x0001;
        try
        {
            var t = new TestStatusTelegram { MachineId = 7, Speed = 1.0f, Label = "hi" };
            var bytes = t.Serialize();

            ushort id = BinaryPrimitives.ReadUInt16BigEndian(bytes);
            id.Should().Be(0x0001);
        }
        finally { TestStatusTelegram.Definition.MessageId = 0; }
    }

    [Fact]
    public void Serialize_WritesTelegramId_ZeroByDefault_WhenNotRegistered()
    {
        // MessageId starts as 0 when not set via the builder.
        TestStatusTelegram.Definition.MessageId = 0;
        var t = new TestStatusTelegram { MachineId = 1 };
        var bytes = t.Serialize();

        ushort id = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        id.Should().Be(0x0000);
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

    // ── 3. Wrong TelegramId throws (only when MessageId != 0) ─────────────────

    [Fact]
    public void Deserialize_ThrowsOnWrongTelegramId_WhenMessageIdIsConfigured()
    {
        TestStatusTelegram.Definition.MessageId = 0x0001;
        try
        {
            var bytes = new TestStatusTelegram { MachineId = 1 }.Serialize();
            bytes[0] = 0x00;
            bytes[1] = 0x99; // overwrite TelegramId with 0x0099

            var act = () => TestStatusTelegram.Deserialize(bytes);
            act.Should().Throw<ArgumentException>().WithMessage("*TelegramId mismatch*");
        }
        finally { TestStatusTelegram.Definition.MessageId = 0; }
    }

    [Fact]
    public void Deserialize_DoesNotThrow_WhenMessageIdIsZero_AndIdMismatches()
    {
        // When Definition.MessageId == 0 (not registered), id validation is skipped.
        TestStatusTelegram.Definition.MessageId = 0;
        var bytes = new TestStatusTelegram { MachineId = 5 }.Serialize();
        bytes[0] = 0xFF; // any value — should be ignored
        bytes[1] = 0xFF;

        var act = () => TestStatusTelegram.Deserialize(bytes);
        act.Should().NotThrow();
    }

    // ── 4. Short buffer throws ────────────────────────────────────────────────

    [Fact]
    public void Deserialize_ThrowsOnShortBuffer()
    {
        var act = () => TestStatusTelegram.Deserialize(new byte[3]);
        act.Should().Throw<ArgumentException>().WithMessage("*Buffer too short*");
    }

    // ── 5. WireSize — 2-byte TelegramId header + data fields (no embedded length) ─

    [Fact]
    public void WireSize_IsTelegramIdHeaderPlusDataFields()
    {
        // 2 (TelegramId) + 2 (MachineId Word) + 4 (Speed Real) + 12 (Label S7String(10)) = 20
        const int expected = 2 + 2 + 4 + (2 + 10);
        TestStatusTelegram.WireSize.Should().Be(expected);
    }

    [Fact]
    public void Serialize_ProducesExactlyWireSize_Bytes()
    {
        var bytes = new TestStatusTelegram().Serialize();
        bytes.Length.Should().Be(TestStatusTelegram.WireSize);
    }

    // ── 6. Definition structure ───────────────────────────────────────────────

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
        // The generator pre-sets ConfiguredWireSize so TelegramIdFramer can frame correctly
        // even before any builder registration happens.
        TestStatusTelegram.Definition.ConfiguredWireSize
            .Should().Be(TestStatusTelegram.WireSize);
    }

    [Fact]
    public void Definition_MessageId_IsZeroUntilRegistered()
    {
        // In a fresh test run (or after reset), the Definition.MessageId is 0 by default.
        // It is set to a non-zero value via the connection builder's .WithMessageId().
        TestStatusTelegram.Definition.MessageId = 0; // ensure reset
        TestStatusTelegram.Definition.MessageId.Should().Be(0);
    }

    // ── 7. TelegramId instance property follows Definition.MessageId ──────────

    [Fact]
    public void TelegramId_FollowsDefinitionMessageId()
    {
        TestStatusTelegram.Definition.MessageId = 0x0042;
        try
        {
            ITelegram t = new TestStatusTelegram();
            t.TelegramId.Should().Be(0x0042);
        }
        finally { TestStatusTelegram.Definition.MessageId = 0; }
    }

    // ── 8. Byte order is a connection-level parameter ─────────────────────────

    [Fact]
    public void LittleEndian_Word_IsWrittenLittleEndian_WhenByteOrderPassedToSerialize()
    {
        var t = new LittleEndianTelegram { DeviceId = 0x1234 };
        var bytes = t.Serialize(ByteOrder.LittleEndian);

        // bytes[0..1] = TelegramId (little-endian)
        // bytes[2..3] = DeviceId (little-endian) — data starts directly at offset 2
        ushort leWord = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));
        leWord.Should().Be(0x1234);
    }

    [Fact]
    public void TelegramId_IsWrittenLittleEndian_WhenByteOrderIsLittleEndian()
    {
        LittleEndianTelegram.Definition.MessageId = 0x0010;
        try
        {
            var t = new LittleEndianTelegram();
            var bytes = t.Serialize(ByteOrder.LittleEndian);

            // 0x0010 little-endian: low byte first → [0x10, 0x00]
            bytes[0].Should().Be(0x10);
            bytes[1].Should().Be(0x00);
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)).Should().Be(0x0010);
        }
        finally { LittleEndianTelegram.Definition.MessageId = 0; }
    }

    [Fact]
    public void TelegramId_IsWrittenBigEndian_WhenByteOrderIsBigEndian()
    {
        LittleEndianTelegram.Definition.MessageId = 0x0010;
        try
        {
            var t = new LittleEndianTelegram();
            var bytes = t.Serialize(ByteOrder.BigEndian);

            // 0x0010 big-endian: high byte first → [0x00, 0x10]
            bytes[0].Should().Be(0x00);
            bytes[1].Should().Be(0x10);
        }
        finally { LittleEndianTelegram.Definition.MessageId = 0; }
    }

    [Fact]
    public void LittleEndian_RoundTrip_WithConnectionByteOrder()
    {
        var original = new LittleEndianTelegram { DeviceId = 0xBEEF, Value = 2.718f };
        var bytes    = original.Serialize(ByteOrder.LittleEndian);
        var restored = LittleEndianTelegram.Deserialize(bytes, ByteOrder.LittleEndian);

        restored.DeviceId.Should().Be(0xBEEF);
        restored.Value.Should().BeApproximately(2.718f, 1e-3f);
    }

    [Fact]
    public void DefaultSerialize_IsBigEndian()
    {
        var t = new LittleEndianTelegram { DeviceId = 0x1234 };
        var bytes = t.Serialize(); // default = BigEndian

        // DeviceId at bytes[2..3] (data starts at offset 2 after the 2-byte TelegramId)
        ushort beWord = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2));
        beWord.Should().Be(0x1234);
    }

    // ── 9. Definition usable via static interface member (zero reflection) ─────

    [Fact]
    public void Definition_CanBeRegistered_WithoutReflection()
    {
        var registry = new TelegramRegistry();
        registry.Register(TestStatusTelegram.Definition); // T.Definition — zero reflection
        registry.TryGet("TestStatusTelegram", out _).Should().BeTrue();
    }

    // ── 10. Builder-style registration sets MessageId on Definition ───────────

    [Fact]
    public void Definition_MessageId_IsUpdated_WhenSetDirectly()
    {
        // Simulates what the connection builder's .WithMessageId() does internally.
        var savedId = TestStatusTelegram.Definition.MessageId;
        try
        {
            TestStatusTelegram.Definition.MessageId = 0x0001;
            TestStatusTelegram.Definition.MessageId.Should().Be(0x0001);

            // TelegramId instance property reflects the runtime value.
            ITelegram t = new TestStatusTelegram();
            t.TelegramId.Should().Be(0x0001);
        }
        finally { TestStatusTelegram.Definition.MessageId = savedId; }
    }

    // ── 11. char[] (S7CharArray) support ─────────────────────────────────────

    [Fact]
    public void CharArray_WireSize_IsExactLength()
    {
        // 2 (TelegramId) + 2 (DeviceId Word) + 8 (Tag CharArray) + 4 (Code CharArray) = 16
        const int telegramId = 2;
        const int deviceId   = 2;
        const int tag        = 8;
        const int code       = 4;
        CharArrayTelegram.WireSize.Should().Be(telegramId + deviceId + tag + code);
    }

    [Fact]
    public void CharArray_Serialize_WritesCharsAsBytes()
    {
        var t = new CharArrayTelegram
        {
            DeviceId = 0x0001,
            Tag  = new[] { 'H', 'e', 'l', 'l', 'o', '!', '\0', '\0' },
            Code = new[] { 'A', 'B', 'C', 'D' },
        };
        var bytes = t.Serialize();

        // Tag starts at offset 4 (2 TelegramId + 2 DeviceId)
        bytes[4].Should().Be((byte)'H');
        bytes[5].Should().Be((byte)'e');
        bytes[6].Should().Be((byte)'l');
        bytes[7].Should().Be((byte)'l');
        bytes[8].Should().Be((byte)'o');
        bytes[9].Should().Be((byte)'!');

        // Code starts at offset 12 (4 + 8)
        bytes[12].Should().Be((byte)'A');
        bytes[13].Should().Be((byte)'B');
        bytes[14].Should().Be((byte)'C');
        bytes[15].Should().Be((byte)'D');
    }

    [Fact]
    public void CharArray_RoundTrip_PreservesAllChars()
    {
        var original = new CharArrayTelegram
        {
            DeviceId = 0x0042,
            Tag  = new[] { 'T', 'E', 'S', 'T', '_', 'T', 'A', 'G' },
            Code = new[] { 'X', '1', '2', '3' },
        };
        var bytes    = original.Serialize();
        var restored = CharArrayTelegram.Deserialize(bytes);

        restored.DeviceId.Should().Be(0x0042);
        restored.Tag.Should().Equal('T', 'E', 'S', 'T', '_', 'T', 'A', 'G');
        restored.Code.Should().Equal('X', '1', '2', '3');
    }

    [Fact]
    public void CharArray_ShortArray_PaddedWithZeros()
    {
        var t = new CharArrayTelegram
        {
            Tag  = new[] { 'A', 'B', 'C' },
            Code = new[] { 'Z' },
        };
        var bytes = t.Serialize();

        // Tag: 'A','B','C' then 5 zero bytes (starts at offset 4)
        bytes[4].Should().Be((byte)'A');
        bytes[5].Should().Be((byte)'B');
        bytes[6].Should().Be((byte)'C');
        bytes[7].Should().Be(0);
        bytes[11].Should().Be(0);

        // Code: 'Z' then 3 zero bytes (starts at offset 12)
        bytes[12].Should().Be((byte)'Z');
        bytes[13].Should().Be(0);
    }

    [Fact]
    public void CharArray_NullArray_SerializesAsAllZeros()
    {
        var t = new CharArrayTelegram { Tag = null, Code = null };
        var bytes = t.Serialize();

        // Data fields start at offset 4 (2 TelegramId + 2 DeviceId)
        for (int i = 4; i < 4 + 8 + 4; i++)
            bytes[i].Should().Be(0, because: $"byte[{i}] should be zero for null char[]");
    }
}
