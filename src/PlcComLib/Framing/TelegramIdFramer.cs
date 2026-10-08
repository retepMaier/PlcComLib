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

    // Id-based entries, in registration order.
    private readonly FrameEntry[] _entries;

    // Frame size used when the registry holds only size-based definitions (no MessageId).
    // 0 when id-based framing is used.
    private readonly int _fixedSize;

    private readonly record struct FrameEntry(long MessageId, int WireSize, int IdByteOffset, S7DataType IdDataType, int MinBytes);

    /// <summary>Largest frame this framer can produce; the receive buffer never needs to hold more.</summary>
    public int MaxFrameSize { get; }

    /// <param name="definitions">
    /// The registered telegram definitions. Definitions with a non-zero
    /// <see cref="TelegramDefinition.MessageId"/> are framed by id. If no definition has a
    /// MessageId, all definitions must share a single wire size, which is then used as a
    /// fixed frame size. Mixing both kinds is rejected because a byte stream cannot be split
    /// reliably without an id.
    /// </param>
    /// <param name="byteOrder">
    /// Byte order used to read the TelegramId from the stream. Must match the
    /// byte order configured on the connection.
    /// </param>
    /// <exception cref="ArgumentException">The definitions cannot be framed unambiguously.</exception>
    public TelegramIdFramer(IEnumerable<TelegramDefinition> definitions, ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _byteOrder = byteOrder;

        var entries   = new List<FrameEntry>();
        var sizeBased = new List<TelegramDefinition>();
        foreach (var def in definitions)
        {
            if (def.MessageId == 0)
            {
                // Definitions with neither id nor size can never match anything — ignore them.
                if (def.EffectiveWireSize > 0) sizeBased.Add(def);
                continue;
            }

            if (def.MessageIdByteOffset < 0)
                throw new ArgumentException(
                    $"Telegram '{def.Id}': MessageId byte offset must not be negative (was {def.MessageIdByteOffset}).",
                    nameof(definitions));

            int minBytes = def.MessageIdByteOffset + S7TypeConverter.GetWireSize(def.MessageIdDataType);
            if (def.EffectiveWireSize < minBytes)
                throw new ArgumentException(
                    $"Telegram '{def.Id}': wire size {def.EffectiveWireSize} is too small to contain its MessageId " +
                    $"at offset {def.MessageIdByteOffset} ({minBytes} bytes needed). Declare fields or call WithLength.",
                    nameof(definitions));

            entries.Add(new FrameEntry(def.MessageId, def.EffectiveWireSize, def.MessageIdByteOffset, def.MessageIdDataType, minBytes));
        }

        if (entries.Count > 0 && sizeBased.Count > 0)
            throw new ArgumentException(
                "TCP framing cannot mix telegrams with and without a MessageId: " +
                $"'{string.Join("', '", sizeBased.Select(d => d.Id))}' have no MessageId. " +
                "Call WithMessageId for every registered telegram.",
                nameof(definitions));

        if (sizeBased.Count > 0)
        {
            var sizes = sizeBased.Select(d => d.EffectiveWireSize).Distinct().ToArray();
            if (sizes.Length > 1)
                throw new ArgumentException(
                    "TCP framing without a MessageId requires all telegrams to have the same wire size, " +
                    $"but found sizes {string.Join(", ", sizes)}. Call WithMessageId for every registered telegram.",
                    nameof(definitions));
            _fixedSize = sizes[0];
        }

        _entries     = [.. entries];
        MaxFrameSize = _fixedSize > 0 ? _fixedSize : (entries.Count > 0 ? entries.Max(e => e.WireSize) : 0);
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
    /// <para>
    /// On a <c>false</c> return, <paramref name="consumed"/> is set to <c>1</c> only when the
    /// buffer was long enough to check <em>every</em> registered ID and none matched — the caller
    /// should discard that byte and retry. It remains <c>0</c> when some ID could not be checked
    /// yet, or when an ID matched but the full frame has not arrived (wait for more data).
    /// </para>
    /// <para>
    /// With no registered definitions at all, the whole buffer is returned as one message so
    /// the caller can report it as unknown instead of buffering it forever.
    /// </para>
    /// </remarks>
    public bool TryExtract(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> message, out int consumed)
    {
        message  = default;
        consumed = 0;
        if (buffer.IsEmpty) return false;

        if (_fixedSize > 0)
        {
            if (buffer.Length < _fixedSize) return false;
            message  = buffer[.._fixedSize];
            consumed = _fixedSize;
            return true;
        }

        if (_entries.Length == 0)
        {
            message  = buffer;
            consumed = buffer.Length;
            return true;
        }

        bool allChecked = true;
        foreach (var entry in _entries)
        {
            if (buffer.Length < entry.MinBytes) { allChecked = false; continue; }

            long telegramId = ReadId(buffer, entry.IdByteOffset, entry.IdDataType, _byteOrder);
            if (telegramId != entry.MessageId) continue;

            // ID matched — wait for the full frame.
            if (buffer.Length < entry.WireSize) return false;

            message  = buffer[..entry.WireSize];
            consumed = entry.WireSize;
            return true;
        }

        // Only give up on the first byte once every registered ID has been checked; otherwise a
        // partially received frame whose ID sits at a larger offset would lose its first byte.
        if (allChecked) consumed = 1;
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
            S7DataType.Byte or S7DataType.USInt => buffer[offset],
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
            S7DataType.Byte or S7DataType.USInt => buffer[offset],
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

    /// <summary>
    /// Writes an integer header value (MessageId or length) into <paramref name="buffer"/> at
    /// <paramref name="offset"/>, using the same encoding that <see cref="ReadId"/> reads.
    /// Returns <c>false</c> (and writes nothing) when the field does not fit or the type is not an integer type.
    /// </summary>
    internal static bool TryWriteInteger(Span<byte> buffer, int offset, S7DataType dataType, long value, ByteOrder byteOrder)
    {
        int size = dataType switch
        {
            S7DataType.Byte or S7DataType.USInt or S7DataType.SInt => 1,
            S7DataType.Word or S7DataType.UInt or S7DataType.Int   => 2,
            S7DataType.DWord or S7DataType.DInt                    => 4,
            S7DataType.LWord or S7DataType.LInt                    => 8,
            _                                                      => 0,
        };
        if (size == 0 || offset < 0 || offset + size > buffer.Length) return false;

        var dst = buffer.Slice(offset, size);
        bool le = byteOrder == ByteOrder.LittleEndian;
        switch (size)
        {
            case 1: dst[0] = unchecked((byte)value); break;
            case 2:
                if (le) BinaryPrimitives.WriteUInt16LittleEndian(dst, unchecked((ushort)value));
                else    BinaryPrimitives.WriteUInt16BigEndian(dst, unchecked((ushort)value));
                break;
            case 4:
                if (le) BinaryPrimitives.WriteUInt32LittleEndian(dst, unchecked((uint)value));
                else    BinaryPrimitives.WriteUInt32BigEndian(dst, unchecked((uint)value));
                break;
            default:
                if (le) BinaryPrimitives.WriteInt64LittleEndian(dst, value);
                else    BinaryPrimitives.WriteInt64BigEndian(dst, value);
                break;
        }
        return true;
    }
}
