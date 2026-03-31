using System.Buffers.Binary;

namespace PlcComLib.Framing;

/// <summary>
/// Frames messages with a 4-byte big-endian length prefix.
/// Wire format: [Length:UInt32 BE][Payload:byte*Length]
/// </summary>
public sealed class LengthPrefixFramer : IMessageFramer
{
    public const int HeaderSize = 4;

    public byte[] Frame(ReadOnlySpan<byte> payload)
    {
        var framed = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(framed, (uint)payload.Length);
        payload.CopyTo(framed.AsSpan(HeaderSize));
        return framed;
    }

    public bool TryExtract(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> message, out int consumed)
    {
        message = default;
        consumed = 0;
        if (buffer.Length < HeaderSize) return false;
        uint length = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        int total = HeaderSize + (int)length;
        if (buffer.Length < total) return false;
        message = buffer.Slice(HeaderSize, (int)length);
        consumed = total;
        return true;
    }
}
