using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.SourceGenerator;
using PlcComLib.Telegrams;

// File-scoped namespace must come first; the inline telegram classes live inside this namespace.
namespace PlcComLib.SourceGenerator.Tests;

// ── Inline typed telegram classes (source generator processes these at build time) ──

[S7Telegram]
public partial class TestStatusTelegram
{
    /// <summary>Message identifier — any property name is valid; located by [MsgId].</summary>
    [MsgId(0x0001)] public partial ushort Id { get; }
    /// <summary>Wire length — any property name is valid; located by [MsgLength].</summary>
    [MsgLength]     public partial int    TotalLength { get; }

    [S7Word]              public ushort MachineId { get; set; }
    [S7Real]              public float  Speed     { get; set; }
    [S7String(maxLength: 10)] public string Label { get; set; } = "";
}

/// <summary>Covers every S7 scalar type.</summary>
[S7Telegram]
public partial class FullTypeTelegram
{
    [MsgId(0x0002)] public partial ushort FrameId { get; }
    [MsgLength]     public partial int    FrameLength { get; }

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

/// <summary>
/// Telegram for a non-PLC device (e.g. a Linux sensor board).
/// Byte order (little-endian) is now a connection-level setting, not part of the attribute.
/// </summary>
[S7Telegram]
public partial class LittleEndianTelegram
{
    [MsgId(0x0010)] public partial ushort MsgIdentifier { get; }
    [MsgLength]     public partial int    MsgSize { get; }

    [S7Word] public ushort DeviceId { get; set; }
    [S7Real] public float  Value    { get; set; }
}

/// <summary>Telegram with a fixed-length char[] field.</summary>
[S7Telegram]
public partial class CharArrayTelegram
{
    [MsgId(0x0020)]      public partial ushort PacketId { get; }
    [MsgLength]          public partial int    PacketLength { get; }

    [S7Word]             public ushort        DeviceId { get; set; }
    [S7CharArray(8)]     public char[]?       Tag      { get; set; }
    [S7CharArray(4)]     public char[]?       Code     { get; set; }
}

/// <summary>
/// Telegram where [MsgId] is declared as <c>int</c> (not ushort) and
/// [MsgLength] is declared as <c>ushort</c> (not int).
/// Validates that the generator supports any numeric type for these framing properties.
/// </summary>
[S7Telegram]
public partial class FlexibleTypeTelegram
{
    [MsgId(0x0030)] public partial int    MyId     { get; }
    [MsgLength]     public partial ushort MyLength { get; }

    [S7Word] public ushort SensorId { get; set; }
    [S7DInt] public int    Reading  { get; set; }
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

    // ── 5. WireSize includes 2-byte MessageId header and 2-byte MessageLength ──

    [Fact]
    public void WireSize_IncludesMessageIdHeader()
    {
        // 2 (MessageId) + 2 (MessageLength) + 2 (Word) + 4 (Real) + 12 (S7String maxLen=10) = 22
        const int expected = 2 + 2 + 2 + 4 + (2 + 10);
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

    // ── 7. TelegramId is same as MessageId (ITelegram instance member) ─────────

    [Fact]
    public void TelegramId_MatchesMessageId()
    {
        var t = new TestStatusTelegram();
        ((ITelegram)t).TelegramId.Should().Be(TestStatusTelegram.MessageId);
    }

    // ── 8. Little-endian byte order — now a connection-level parameter ─────────

    [Fact]
    public void LittleEndian_Word_IsWrittenLittleEndian_WhenByteOrderPassedToSerialize()
    {
        var t = new LittleEndianTelegram { DeviceId = 0x1234 };
        // byte order is passed at serialize time (as the connection would pass it)
        var bytes = t.Serialize(ByteOrder.LittleEndian);

        // bytes[0..1] = MessageId (little-endian)
        // bytes[2..3] = MessageLength (little-endian)
        // bytes[4..5] = DeviceId (little-endian)
        ushort leWord = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        leWord.Should().Be(0x1234);
    }

    [Fact]
    public void TelegramId_IsWrittenLittleEndian_WhenByteOrderIsLittleEndian()
    {
        var t = new LittleEndianTelegram();
        var bytes = t.Serialize(ByteOrder.LittleEndian);

        // MessageId 0x0010 in little-endian: low byte first → [0x10, 0x00]
        ushort leMsgId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0));
        leMsgId.Should().Be(LittleEndianTelegram.MessageId);

        // Verify it is NOT big-endian (bytes would differ for 0x0010)
        bytes[0].Should().Be(0x10); // low byte first in little-endian
        bytes[1].Should().Be(0x00);
    }

    [Fact]
    public void TelegramId_IsWrittenBigEndian_WhenByteOrderIsBigEndian()
    {
        var t = new LittleEndianTelegram();
        var bytes = t.Serialize(ByteOrder.BigEndian);

        // MessageId 0x0010 in big-endian: high byte first → [0x00, 0x10]
        ushort beMsgId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0));
        beMsgId.Should().Be(LittleEndianTelegram.MessageId);

        bytes[0].Should().Be(0x00); // high byte first in big-endian
        bytes[1].Should().Be(0x10);
    }

    [Fact]
    public void LittleEndian_RoundTrip_WithConnectionByteOrder()
    {
        var original = new LittleEndianTelegram { DeviceId = 0xBEEF, Value = 2.718f };
        // Simulate a little-endian connection serialising and deserialising
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

        // bytes[4..5] = DeviceId (after MessageId at 0-1 and MessageLength at 2-3)
        ushort beWord = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        beWord.Should().Be(0x1234);
    }

    // ── 9. Definition usable via static interface member (zero reflection) ─────

    [Fact]
    public void Definition_CanBeRegistered_WithoutReflection()
    {
        var registry = new TelegramRegistry();
        registry.Register(TestStatusTelegram.Definition);   // T.Definition — no reflection
        registry.TryGet("TestStatusTelegram", out var def).Should().BeTrue();
        def!.MessageId.Should().Be(0x0001);
    }

    // ── 10. TelegramId alias on TelegramDefinition ────────────────────────────

    [Fact]
    public void Definition_TelegramId_MatchesMessageId()
    {
        var def = TestStatusTelegram.Definition;
        def.TelegramId.Should().Be(def.MessageId);
        def.TelegramId.Should().Be(0x0001);
    }

    // ── 11. [MsgId] flexible property name ───────────────────────────────────

    [Fact]
    public void MsgId_PartialProperty_ReturnsCorrectValue_ViaFlexibleName()
    {
        // 'Id' is the user-chosen name decorated with [MsgId(0x0001)].
        var t = new TestStatusTelegram();
        t.Id.Should().Be(0x0001);
        t.Id.Should().Be(TestStatusTelegram.MessageId);
    }

    [Fact]
    public void MsgId_PartialProperty_MatchesITelegramMessageId()
    {
        ITelegram t = new TestStatusTelegram();
        t.MessageId.Should().Be(0x0001);
        t.MessageId.Should().Be(TestStatusTelegram.MessageId);
    }

    // ── 12. [MsgLength] flexible property name ────────────────────────────────

    [Fact]
    public void MsgLength_PartialProperty_ReturnsWireSize_ViaFlexibleName()
    {
        // 'TotalLength' is the user-chosen name decorated with [MsgLength].
        var t = new TestStatusTelegram();
        t.TotalLength.Should().Be(TestStatusTelegram.WireSize);
        // 2 (MessageId) + 2 (MessageLength) + 2 (Word) + 4 (Real) + 12 (S7String maxLen=10)
        t.TotalLength.Should().Be(22);
    }

    [Fact]
    public void MsgLength_PartialProperty_MatchesITelegramLength()
    {
        ITelegram t = new TestStatusTelegram();
        t.Length.Should().Be(TestStatusTelegram.WireSize);
    }

    // ── 13. char[] (S7CharArray) support ─────────────────────────────────────

    [Fact]
    public void CharArray_WireSize_IsExactLength()
    {
        // 2 (MessageId) + 2 (MessageLength) + 2 (DeviceId Word) + 8 (Tag CharArray) + 4 (Code CharArray)
        const int msgId     = 2;
        const int msgLength = 2;
        const int deviceId  = 2;
        const int tag       = 8;
        const int code      = 4;
        CharArrayTelegram.WireSize.Should().Be(msgId + msgLength + deviceId + tag + code);
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

        // Tag starts at offset 6 (2 MessageId + 2 MessageLength + 2 DeviceId)
        bytes[6].Should().Be((byte)'H');
        bytes[7].Should().Be((byte)'e');
        bytes[8].Should().Be((byte)'l');
        bytes[9].Should().Be((byte)'l');
        bytes[10].Should().Be((byte)'o');
        bytes[11].Should().Be((byte)'!');

        // Code starts at offset 14 (6 + 8)
        bytes[14].Should().Be((byte)'A');
        bytes[15].Should().Be((byte)'B');
        bytes[16].Should().Be((byte)'C');
        bytes[17].Should().Be((byte)'D');
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
        // Provide only 3 chars for an 8-element Tag field
        var t = new CharArrayTelegram
        {
            Tag  = new[] { 'A', 'B', 'C' },
            Code = new[] { 'Z' },
        };
        var bytes = t.Serialize();

        // Tag: 'A','B','C' then 5 zero bytes (starts at offset 6)
        bytes[6].Should().Be((byte)'A');
        bytes[7].Should().Be((byte)'B');
        bytes[8].Should().Be((byte)'C');
        bytes[9].Should().Be(0);
        bytes[13].Should().Be(0);

        // Code: 'Z' then 3 zero bytes (starts at offset 14)
        bytes[14].Should().Be((byte)'Z');
        bytes[15].Should().Be(0);
    }

    [Fact]
    public void CharArray_NullArray_SerializesAsAllZeros()
    {
        var t = new CharArrayTelegram { Tag = null, Code = null };
        var bytes = t.Serialize();

        // Data fields start at offset 6 (2 MessageId + 2 MessageLength + 2 DeviceId)
        for (int i = 6; i < 6 + 8 + 4; i++)
            bytes[i].Should().Be(0, because: $"byte[{i}] should be zero for null char[]");
    }

    [Fact]
    public void CharArray_MsgId_ViaDifferentPropertyName()
    {
        // PacketId is the user-chosen [MsgId] property name on CharArrayTelegram
        var t = new CharArrayTelegram();
        t.PacketId.Should().Be(0x0020);
        ((ITelegram)t).MessageId.Should().Be(0x0020);
    }

    [Fact]
    public void CharArray_MsgLength_ViaDifferentPropertyName()
    {
        // PacketLength is the user-chosen [MsgLength] property name on CharArrayTelegram
        var t = new CharArrayTelegram();
        t.PacketLength.Should().Be(CharArrayTelegram.WireSize);
        ((ITelegram)t).Length.Should().Be(CharArrayTelegram.WireSize);
    }

    // ── 14. [MsgId] and [MsgLength] with flexible property types ─────────────

    [Fact]
    public void FlexibleType_MsgId_DeclaresAsInt_ReturnsCorrectValue()
    {
        // MyId is declared as 'int' (not ushort) but [MsgId(0x0030)] is set.
        var t = new FlexibleTypeTelegram();
        t.MyId.Should().Be(0x0030);
        t.MyId.Should().Be((int)FlexibleTypeTelegram.MessageId);
        ((ITelegram)t).MessageId.Should().Be(0x0030);
    }

    [Fact]
    public void FlexibleType_MsgLength_DeclaresAsUshort_ReturnsWireSize()
    {
        // MyLength is declared as 'ushort' (not int) but [MsgLength] is applied.
        // WireSize = 2 (MessageId) + 2 (MessageLength) + 2 (SensorId Word) + 4 (Reading DInt) = 10
        var t = new FlexibleTypeTelegram();
        t.MyLength.Should().Be((ushort)FlexibleTypeTelegram.WireSize);
        t.MyLength.Should().Be(10);
        ((ITelegram)t).Length.Should().Be(FlexibleTypeTelegram.WireSize);
    }

    [Fact]
    public void FlexibleType_RoundTrip_WorksCorrectly()
    {
        var original = new FlexibleTypeTelegram { SensorId = 0xABCD, Reading = -42 };
        var bytes    = original.Serialize();
        var restored = FlexibleTypeTelegram.Deserialize(bytes);

        restored.SensorId.Should().Be(0xABCD);
        restored.Reading.Should().Be(-42);
    }

    [Fact]
    public void FlexibleType_MsgId_IsWrittenCorrectlyOnWire()
    {
        var t = new FlexibleTypeTelegram();
        var bytes = t.Serialize();

        // MessageId 0x0030 is still written as 2-byte Word at offset 0 (big-endian)
        ushort wireId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0));
        wireId.Should().Be(0x0030);
    }
}
