using System.Buffers.Binary;

namespace PlcComLib.Framing;

/// <summary>
/// Frames messages by embedding the total frame length in the first 2 bytes of the payload itself.
/// </summary>
/// <remarks>
/// <para>
/// Wire format: <c>[TotalLength: UInt16 BE (2 bytes)][Payload: byte * (TotalLength - 2)]</c>.
/// <c>TotalLength</c> is the total number of bytes in the frame including the 2-byte length field.
/// For example, a 10-byte payload body produces a 12-byte frame:
/// <c>[0x00][0x0C][10 payload bytes]</c>.
/// </para>
/// <para>
/// Use this framer when communicating with a Siemens PLC via TSEND/TRCV, where the length
/// field is a plain <c>WORD</c> variable at the start of the DB block and is part of the raw payload.
/// </para>
/// </remarks>
public sealed class PayloadLengthFramer : IMessageFramer
{
    public const int HeaderSize = 2;

    public byte[] Frame(ReadOnlySpan<byte> payload)
    {
        var framed = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)(HeaderSize + payload.Length));
        payload.CopyTo(framed.AsSpan(HeaderSize));
        return framed;
    }

    public bool TryExtract(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> message, out int consumed)
    {
        message = default;
        consumed = 0;
        if (buffer.Length < HeaderSize) return false;
        int totalLength = BinaryPrimitives.ReadUInt16BigEndian(buffer);
        if (buffer.Length < totalLength) return false;
        message = buffer.Slice(HeaderSize, totalLength - HeaderSize);
        consumed = totalLength;
        return true;
    }
}
