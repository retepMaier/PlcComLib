using System.Buffers.Binary;
using System.Collections.Generic;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.Framing;

/// <summary>
/// Frames messages by reading the TelegramId from the first two bytes of the payload
/// and using the pre-registered wire size for that TelegramId to determine frame boundaries.
/// </summary>
/// <remarks>
/// <para>
/// Wire format: <c>[TelegramId: UInt16 (2 bytes)][Payload: byte * (WireSize - 2)]</c>.
/// The TelegramId is the first two bytes of every message and identifies the telegram type.
/// The expected total frame size is looked up from the registered <see cref="TelegramDefinition"/> list.
/// </para>
/// <para>
/// Use this framer when communicating with a Siemens PLC via TSEND/TRCV and the DB block
/// starts with the telegram identifier (via the <c>[MsgId]</c> attribute). Both sides must
/// agree on the same set of telegram definitions.
/// </para>
/// </remarks>
public sealed class TelegramIdFramer : IMessageFramer
{
    private readonly ByteOrder _byteOrder;
    private readonly IReadOnlyDictionary<ushort, int> _sizeByTelegramId;

    /// <param name="definitions">
    /// The registered telegram definitions. Only definitions with a non-zero
    /// <see cref="TelegramDefinition.MessageId"/> are used for frame-size lookup.
    /// </param>
    /// <param name="byteOrder">
    /// Byte order used to read the 2-byte TelegramId from the stream. Must match the
    /// byte order configured on the connection.
    /// </param>
    public TelegramIdFramer(IEnumerable<TelegramDefinition> definitions, ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _byteOrder = byteOrder;

        var dict = new Dictionary<ushort, int>();
        foreach (var def in definitions)
        {
            if (def.MessageId != 0)
                dict[def.MessageId] = def.TotalWireSize;
        }
        _sizeByTelegramId = dict;
    }

    /// <summary>
    /// Returns the payload as-is.
    /// The TelegramId is already the first two bytes of every serialised telegram,
    /// so no additional framing header is required.
    /// </summary>
    public byte[] Frame(ReadOnlySpan<byte> payload) => payload.ToArray();

    /// <summary>
    /// Attempts to extract a complete frame from <paramref name="buffer"/>.
    /// Reads the first 2 bytes as a TelegramId, looks up the expected total wire size,
    /// and waits until that many bytes are available.
    /// </summary>
    public bool TryExtract(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> message, out int consumed)
    {
        message  = default;
        consumed = 0;

        if (buffer.Length < 2) return false;

        ushort telegramId = _byteOrder == ByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt16BigEndian(buffer);

        if (!_sizeByTelegramId.TryGetValue(telegramId, out int expectedSize)) return false;
        if (buffer.Length < expectedSize) return false;

        message  = buffer.Slice(0, expectedSize);
        consumed = expectedSize;
        return true;
    }
}
