using System.Buffers.Binary;
using System.Collections.Generic;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.Framing;

/// <summary>
/// Frames messages by reading the TelegramId from a configured byte offset of the payload
/// and using the pre-registered wire size for that TelegramId to determine frame boundaries.
/// </summary>
/// <remarks>
/// <para>
/// Default wire format: <c>[TelegramId: UInt16 (2 bytes)][Data fields…]</c>.
/// The offset and data type used to read the TelegramId can be customised per-definition via
/// <c>.WithMessageId&lt;TType&gt;(id, byteOffset)</c> on the connection builder.
/// The expected total frame size is looked up from the registered <see cref="TelegramDefinition"/> list.
/// </para>
/// <para>
/// This is the default framer when using source-generated typed telegrams registered via
/// <c>RegisterTelegram&lt;T&gt;().WithMessageId&lt;TType&gt;(id, byteOffset).WithLength&lt;TType&gt;(length, byteOffset)</c>
/// on the connection builder.
/// Both sides of the connection must agree on the same set of telegram definitions.
/// </para>
/// </remarks>
public sealed class TelegramIdFramer : IMessageFramer
{
    private readonly ByteOrder _byteOrder;

    // MessageId → (EffectiveWireSize, MessageIdByteOffset, MessageIdDataType)
    private readonly IReadOnlyDictionary<long, FrameEntry> _entryByMessageId;

    private readonly record struct FrameEntry(int WireSize, int IdByteOffset, S7DataType IdDataType);

    /// <param name="definitions">
    /// The registered telegram definitions. Only definitions with a non-zero
    /// <see cref="TelegramDefinition.MessageId"/> are used for frame-size lookup.
    /// </param>
    /// <param name="byteOrder">
    /// Byte order used to read the TelegramId from the stream. Must match the
    /// byte order configured on the connection.
    /// </param>
    public TelegramIdFramer(IEnumerable<TelegramDefinition> definitions, ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _byteOrder = byteOrder;

        var dict = new Dictionary<long, FrameEntry>();
        foreach (var def in definitions)
        {
            if (def.MessageId != 0)
                dict[def.MessageId] = new FrameEntry(def.EffectiveWireSize, def.MessageIdByteOffset, def.MessageIdDataType);
        }
        _entryByMessageId = dict;
    }

    /// <summary>
    /// Returns the payload as-is.
    /// The TelegramId is already embedded in the serialised telegram payload,
    /// so no additional framing header is required.
    /// </summary>
    public byte[] Frame(ReadOnlySpan<byte> payload) => payload.ToArray();

    /// <summary>
    /// Attempts to extract a complete frame from <paramref name="buffer"/>.
    /// Reads the TelegramId from the configured byte offset and data type, looks up the
    /// expected total wire size, and waits until that many bytes are available.
    /// </summary>
    /// <remarks>
    /// On a <c>false</c> return, <paramref name="consumed"/> is set to <c>1</c> when the buffer
    /// contained enough bytes to check at least one registered ID but none matched — the caller
    /// should discard those bytes and retry. <paramref name="consumed"/> remains <c>0</c> when
    /// there were insufficient bytes to evaluate any registered ID (wait for more data), or when
    /// an ID matched but the full frame has not yet arrived (wait for more data).
    /// </remarks>
    public bool TryExtract(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> message, out int consumed)
    {
        message  = default;
        consumed = 0;

        bool anyIdChecked = false;

        // Try each registered definition: find the first whose ID matches at its configured offset.
        foreach (var (msgId, entry) in _entryByMessageId)
        {
            int idSize = S7TypeConverter.GetWireSize(entry.IdDataType);
            int minBytes = entry.IdByteOffset + idSize;
            if (buffer.Length < minBytes) continue;

            anyIdChecked = true;
            long telegramId = ReadId(buffer, entry.IdByteOffset, entry.IdDataType);
            if (telegramId != msgId) continue;

            // ID matched — wait for the full frame.
            if (buffer.Length < entry.WireSize) return false;

            message  = buffer.Slice(0, entry.WireSize);
            consumed = entry.WireSize;
            return true;
        }

        // If at least one ID was read but nothing matched, signal that 1 byte should be
        // discarded so the caller can scan forward and avoid an unbounded buffer.
        if (anyIdChecked) consumed = 1;
        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a numeric id value from <paramref name="buffer"/> at the given
    /// <paramref name="offset"/> using the specified <paramref name="dataType"/> and byte order.
    /// Used by the framer and by dispatch layers to identify an incoming telegram.
    /// </summary>
    public static long ReadId(ReadOnlySpan<byte> buffer, int offset, S7DataType dataType,ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        bool le = byteOrder == ByteOrder.LittleEndian;
        return dataType switch
        {
            S7DataType.Byte  => buffer[offset],
            S7DataType.SInt  => (sbyte)buffer[offset],
            S7DataType.Int   => le
                ? BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(offset, 2))
                : BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(offset, 2)),
            S7DataType.DWord => (long)(le
                ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset, 4))
                : BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(offset, 4))),
            S7DataType.DInt  => le
                ? BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4))
                : BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(offset, 4)),
            S7DataType.LWord => (long)(le
                ? BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset, 8))
                : BinaryPrimitives.ReadUInt64BigEndian(buffer.Slice(offset, 8))),
            S7DataType.LInt  => le
                ? BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(offset, 8))
                : BinaryPrimitives.ReadInt64BigEndian(buffer.Slice(offset, 8)),
            // Default: Word (UInt16)
            _                => le
                ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset, 2))
                : BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2)),
        };
    }

    // Private instance wrapper forwarding to the static method for use by TryExtract.
    private long ReadId(ReadOnlySpan<byte> buffer, int offset, S7DataType dataType)
        => ReadId(buffer, offset, dataType, _byteOrder);

    /// <summary>
    /// Reads an integer length value from <paramref name="buffer"/> at the given
    /// <paramref name="offset"/> using the specified <paramref name="dataType"/> and byte order.
    /// Used by dispatch layers to validate the embedded length field.
    /// </summary>
    public static long ReadLength(ReadOnlySpan<byte> buffer, int offset, S7DataType dataType, ByteOrder byteOrder)
    {
        bool le = byteOrder == ByteOrder.LittleEndian;
        return dataType switch
        {
            S7DataType.Byte  => buffer[offset],
            S7DataType.SInt  => (sbyte)buffer[offset],
            S7DataType.Int   => le
                ? BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(offset, 2))
                : BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(offset, 2)),
            S7DataType.DWord => le
                ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset, 4))
                : BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(offset, 4)),
            S7DataType.DInt  => le
                ? BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4))
                : BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(offset, 4)),
            S7DataType.LWord => (long)(le
                ? BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset, 8))
                : BinaryPrimitives.ReadUInt64BigEndian(buffer.Slice(offset, 8))),
            S7DataType.LInt  => le
                ? BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(offset, 8))
                : BinaryPrimitives.ReadInt64BigEndian(buffer.Slice(offset, 8)),
            // Default: Word (UInt16)
            _                => le
                ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset, 2))
                : BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2)),
        };
    }
}

