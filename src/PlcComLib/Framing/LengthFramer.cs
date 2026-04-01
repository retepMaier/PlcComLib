using System.Buffers.Binary;
using PlcComLib.DataTypes;

namespace PlcComLib.Framing;

/// <summary>
/// Frames messages using the telegram length embedded in the payload at bytes 2–3 (after the 2-byte TelegramId).
/// </summary>
/// <remarks>
/// <para>
/// Wire format: <c>[TelegramId: UInt16 (2 bytes)][TotalLength: UInt16 (2 bytes)][Data fields…]</c>.
/// <c>TotalLength</c> is the total number of bytes in the frame including both the TelegramId and TotalLength fields.
/// Both values are already embedded in the serialised telegram payload via the <c>[MsgId]</c> and
/// <c>[MsgLength]</c> attributes — no external header is added.
/// </para>
/// <para>
/// Use this framer when communicating with a Siemens PLC via TSEND/TRCV and the DB block starts
/// with the telegram identifier (<c>[MsgId]</c>) followed by the total message length (<c>[MsgLength]</c>).
/// </para>
/// </remarks>
public sealed class LengthFramer : IMessageFramer
{
    private readonly ByteOrder _byteOrder;

    /// <param name="byteOrder">
    /// Byte order used to read the 2-byte TotalLength from the stream. Must match the
    /// byte order configured on the connection.
    /// </param>
    public LengthFramer(ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        _byteOrder = byteOrder;
    }

    /// <summary>
    /// Returns the payload as-is.
    /// Both the TelegramId and TotalLength are already the first four bytes of every
    /// serialised telegram, so no additional framing header is required.
    /// </summary>
    public byte[] Frame(ReadOnlySpan<byte> payload) => payload.ToArray();

    /// <summary>
    /// Attempts to extract a complete frame from <paramref name="buffer"/>.
    /// Reads bytes 2–3 as a <c>TotalLength</c> (UInt16) and waits until that many bytes are available.
    /// </summary>
    public bool TryExtract(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> message, out int consumed)
    {
        message  = default;
        consumed = 0;

        if (buffer.Length < 4) return false; // Need TelegramId (2) + TotalLength (2)

        int totalLength = _byteOrder == ByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2))
            : BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2));

        if (totalLength < 4) return false; // Sanity check: must be at least the header
        if (buffer.Length < totalLength) return false;

        message  = buffer.Slice(0, totalLength);
        consumed = totalLength;
        return true;
    }
}
