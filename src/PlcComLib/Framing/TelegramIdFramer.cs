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
/// Wire format: <c>[TelegramId: UInt16 (2 bytes)][Data fields…]</c>.
/// The TelegramId is the first two bytes of every message and identifies the telegram type.
/// The expected total frame size is looked up from the registered <see cref="TelegramDefinition"/> list.
/// Both <see cref="TelegramDefinition.TotalWireSize"/> (from field definitions) and
/// <see cref="TelegramDefinition.ConfiguredWireSize"/> (from <c>.WithLength(size)</c>) are considered.
/// </para>
/// <para>
/// This is the default framer when using source-generated typed telegrams registered via
/// <c>RegisterTelegram&lt;T&gt;().WithMessageId(id).WithLength(size)</c> on the connection builder.
/// Both sides of the connection must agree on the same set of telegram definitions.
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
                dict[def.MessageId] = def.EffectiveWireSize;
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
