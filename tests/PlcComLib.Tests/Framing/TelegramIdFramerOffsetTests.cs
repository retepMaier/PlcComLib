using System.Buffers.Binary;
using FluentAssertions;
using PlcComLib.Core.PlcTypes;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Telegrams;
using Xunit;

namespace PlcComLib.Tests.Framing;

/// <summary>
/// Tests for the generic <c>.WithMessageId&lt;TType&gt;(id, byteOffset)</c> and
/// <c>.WithLength&lt;TType&gt;(length, byteOffset)</c> framing configuration.
/// </summary>
public class TelegramIdFramerOffsetTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static byte[] MakeBuffer(int totalSize, ushort idAtOffset0, ushort lengthAtOffset2,
        ByteOrder order = ByteOrder.BigEndian)
    {
        var buf = new byte[totalSize];
        if (order == ByteOrder.LittleEndian)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), idAtOffset0);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), lengthAtOffset2);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0), idAtOffset0);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2), lengthAtOffset2);
        }
        return buf;
    }

    // ── 1. Custom MessageId byte offset ───────────────────────────────────────

    [Fact]
    public void WithMessageIdOffset_FramerReadsIdFromConfiguredOffset()
    {
        // ID at offset 2 (S7Word), wire size = 8
        var def = new TelegramDefinition
        {
            Id                  = "T",
            MessageId           = 0x00FF,
            MessageIdByteOffset = 2,
            MessageIdDataType   = S7DataType.Word,
            ConfiguredWireSize  = 8,
        };
        var framer = new TelegramIdFramer([def]);

        // Build a buffer that has 0x00FF at bytes 2–3
        var buf = new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2), 0x00FF);

        framer.TryExtract(buf, out var message, out int consumed).Should().BeTrue();
        message.Length.Should().Be(8);
        consumed.Should().Be(8);
    }

    [Fact]
    public void WithMessageIdOffset_WrongIdAtOffset_ReturnsFalse()
    {
        var def = new TelegramDefinition
        {
            Id                  = "T",
            MessageId           = 0x00FF,
            MessageIdByteOffset = 2,
            MessageIdDataType   = S7DataType.Word,
            ConfiguredWireSize  = 8,
        };
        var framer = new TelegramIdFramer([def]);

        // Put 0x0001 at offset 2 (wrong ID)
        var buf = new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2), 0x0001);

        framer.TryExtract(buf, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void WithMessageIdOffset_BufferTooShortToReadId_ReturnsFalse()
    {
        // ID at offset 4, wire size = 10 → need at least 6 bytes to read the id
        var def = new TelegramDefinition
        {
            Id                  = "T",
            MessageId           = 0x0001,
            MessageIdByteOffset = 4,
            MessageIdDataType   = S7DataType.Word,
            ConfiguredWireSize  = 10,
        };
        var framer = new TelegramIdFramer([def]);

        // Only 4 bytes — cannot even reach the id field
        framer.TryExtract(new byte[4], out _, out _).Should().BeFalse();
    }

    // ── 2. S7Int (signed 16-bit) MessageId type ───────────────────────────────

    [Fact]
    public void WithMessageIdType_S7Int_ReadsSignedWord()
    {
        // Use S7DataType.Int so the framer reads a signed short; value 0x0001 still fits.
        var def = new TelegramDefinition
        {
            Id                  = "T",
            MessageId           = 0x0001,
            MessageIdByteOffset = 0,
            MessageIdDataType   = S7DataType.Int,
            ConfiguredWireSize  = 6,
        };
        var framer = new TelegramIdFramer([def]);

        var buf = new byte[6];
        BinaryPrimitives.WriteInt16BigEndian(buf.AsSpan(0), 0x0001);

        framer.TryExtract(buf, out _, out int consumed).Should().BeTrue();
        consumed.Should().Be(6);
    }

    // ── 3. Default offset behaviour (backward compat) ─────────────────────────

    [Fact]
    public void DefaultOffset_IsZero_AndDefaultType_IsWord()
    {
        var def = new TelegramDefinition
        {
            Id        = "T",
            MessageId = 0x0042,
            // MessageIdByteOffset and MessageIdDataType use defaults
            ConfiguredWireSize = 4,
        };

        def.MessageIdByteOffset.Should().Be(0);
        def.MessageIdDataType.Should().Be(S7DataType.Word);
        def.LengthByteOffset.Should().Be(-1);   // no length validation by default
    }

    [Fact]
    public void DefaultOffset_FramerBehavesAsLegacy()
    {
        var def = new TelegramDefinition
        {
            Id                 = "T",
            MessageId          = 0x0042,
            ConfiguredWireSize = 4,
        };
        var framer = new TelegramIdFramer([def]);

        var buf = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0), 0x0042);

        framer.TryExtract(buf, out _, out int consumed).Should().BeTrue();
        consumed.Should().Be(4);
    }

    // ── 4. LengthByteOffset / LengthDataType on TelegramDefinition ───────────

    [Fact]
    public void LengthByteOffset_DefaultIsNegativeOne_NoValidation()
    {
        var def = new TelegramDefinition { Id = "T" };
        def.LengthByteOffset.Should().Be(-1);
        def.LengthDataType.Should().Be(S7DataType.Word);
    }

    [Fact]
    public void TelegramDefinition_AcceptsLengthFieldConfig()
    {
        var def = new TelegramDefinition
        {
            Id                 = "T",
            MessageId          = 0x0001,
            ConfiguredWireSize = 16,
            LengthByteOffset   = 2,
            LengthDataType     = S7DataType.Int,
        };

        def.LengthByteOffset.Should().Be(2);
        def.LengthDataType.Should().Be(S7DataType.Int);
        def.ConfiguredWireSize.Should().Be(16);
    }

    // ── 5. ReadLength helper ──────────────────────────────────────────────────

    [Fact]
    public void ReadLength_Word_BigEndian_ReadsCorrectly()
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2), 16);

        long result = TelegramIdFramer.ReadLength(buf, offset: 2, S7DataType.Word, ByteOrder.BigEndian);
        result.Should().Be(16);
    }

    [Fact]
    public void ReadLength_Int_BigEndian_ReadsSignedCorrectly()
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt16BigEndian(buf.AsSpan(2), 16);

        long result = TelegramIdFramer.ReadLength(buf, offset: 2, S7DataType.Int, ByteOrder.BigEndian);
        result.Should().Be(16);
    }

    [Fact]
    public void ReadLength_Int_LittleEndian_ReadsSignedCorrectly()
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(2), 16);

        long result = TelegramIdFramer.ReadLength(buf, offset: 2, S7DataType.Int, ByteOrder.LittleEndian);
        result.Should().Be(16);
    }

    // ── 6. IS7FramingType markers ─────────────────────────────────────────────

    [Fact]
    public void S7Word_FramingType_HasCorrectDataType()
        => S7Word.DataType.Should().Be(S7DataType.Word);

    [Fact]
    public void S7Int_FramingType_HasCorrectDataType()
        => S7Int.DataType.Should().Be(S7DataType.Int);

    [Fact]
    public void S7DWord_FramingType_HasCorrectDataType()
        => S7DWord.DataType.Should().Be(S7DataType.DWord);

    [Fact]
    public void S7DInt_FramingType_HasCorrectDataType()
        => S7DInt.DataType.Should().Be(S7DataType.DInt);

    [Fact]
    public void S7Byte_FramingType_HasCorrectDataType()
        => S7Byte.DataType.Should().Be(S7DataType.Byte);

    [Fact]
    public void S7SInt_FramingType_HasCorrectDataType()
        => S7SInt.DataType.Should().Be(S7DataType.SInt);

    [Fact]
    public void S7LWord_FramingType_HasCorrectDataType()
        => S7LWord.DataType.Should().Be(S7DataType.LWord);

    [Fact]
    public void S7LInt_FramingType_HasCorrectDataType()
        => S7LInt.DataType.Should().Be(S7DataType.LInt);
}

/// <summary>
/// Tests for length-field validation (dispatch layer) exercised via
/// <c>.WithLength&lt;TType&gt;(length, byteOffset)</c>.
/// </summary>
public class LengthFieldValidationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    // Build a TelegramDefinition that matches MachineStatus-style layout:
    //   [Word ID at 0][Int Length at 2][…data…]
    private static TelegramDefinition BuildDef(int configuredLength)
        => new()
        {
            Id                 = "MachineStatus",
            MessageId          = 0x0001,
            ConfiguredWireSize = configuredLength,
            LengthByteOffset   = 2,
            LengthDataType     = S7DataType.Int,
            Fields             = [], // hand-crafted; no field list needed
        };

    // Build a raw payload with the ID and the length embedded.
    private static byte[] MakePayload(ushort id, short embeddedLength, int totalSize)
    {
        var buf = new byte[totalSize];
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0), id);
        BinaryPrimitives.WriteInt16BigEndian(buf.AsSpan(2), embeddedLength);
        return buf;
    }

    // ── 1. No length offset — no validation ───────────────────────────────────

    [Fact]
    public void NoLengthOffset_DispatchSucceeds_WithoutValidation()
    {
        // LengthByteOffset = -1 (default) → ValidateLength always returns true
        var def = new TelegramDefinition
        {
            Id                 = "T",
            MessageId          = 0x0001,
            ConfiguredWireSize = 8,
        };
        def.LengthByteOffset.Should().Be(-1);
    }

    // ── 2. Matching length → valid ────────────────────────────────────────────

    [Fact]
    public void LengthFieldMatches_ValidateLength_ReturnsTrue()
    {
        var def = BuildDef(configuredLength: 16);
        var payload = MakePayload(id: 0x0001, embeddedLength: 16, totalSize: 16);

        bool result = InvokeValidateLength(def, payload, ByteOrder.BigEndian);
        result.Should().BeTrue();
    }

    // ── 3. Mismatching length → invalid, event fired ──────────────────────────

    [Fact]
    public void LengthFieldMismatch_ValidateLength_ReturnsFalse_AndFiresEvent()
    {
        var def = BuildDef(configuredLength: 16);
        // Embed a wrong length of 99 bytes
        var payload = MakePayload(id: 0x0001, embeddedLength: 99, totalSize: 16);

        bool eventFired = false;
        bool result = InvokeValidateLength(def, payload, ByteOrder.BigEndian,
            onUnknown: _ => eventFired = true);

        result.Should().BeFalse();
        eventFired.Should().BeTrue();
    }

    // ── 4. Payload too short to read length field ─────────────────────────────

    [Fact]
    public void PayloadTooShortForLengthField_ValidateLength_ReturnsFalse_AndFiresEvent()
    {
        // Length field at offset 2 (S7Int = 2 bytes) → need at least 4 bytes
        var def = BuildDef(configuredLength: 16);
        var shortPayload = new byte[2]; // only 2 bytes — can't reach offset 2+2

        bool eventFired = false;
        bool result = InvokeValidateLength(def, shortPayload, ByteOrder.BigEndian,
            onUnknown: _ => eventFired = true);

        result.Should().BeFalse();
        eventFired.Should().BeTrue();
    }

    // ── 5. Little-endian length field ─────────────────────────────────────────

    [Fact]
    public void LengthFieldMatches_LittleEndian_ReturnsTrue()
    {
        var def = BuildDef(configuredLength: 16);
        var payload = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), 0x0001); // id
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(2), 16);      // length

        bool result = InvokeValidateLength(def, payload, ByteOrder.LittleEndian);
        result.Should().BeTrue();
    }

    // ── Internal helper — calls the same logic as the dispatch layer ──────────

    private static bool InvokeValidateLength(
        TelegramDefinition def,
        byte[] payload,
        ByteOrder byteOrder,
        Action<byte[]>? onUnknown = null)
    {
        if (def.LengthByteOffset < 0) return true;

        int fieldEnd = S7TypeConverter.GetWireSize(def.LengthDataType) + def.LengthByteOffset;
        if (payload.Length < fieldEnd)
        {
            onUnknown?.Invoke(payload);
            return false;
        }

        long received = TelegramIdFramer.ReadLength(payload, def.LengthByteOffset, def.LengthDataType, byteOrder);
        if (received != def.ConfiguredWireSize)
        {
            onUnknown?.Invoke(payload);
            return false;
        }

        return true;
    }
}
