namespace PlcComLib.Framing;

/// <summary>
/// Frames messages with a fixed payload length (no framing overhead).
/// </summary>
public sealed class FixedLengthFramer : IMessageFramer
{
    private readonly int _length;

    public FixedLengthFramer(int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.");
        _length = length;
    }

    public byte[] Frame(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != _length)
            throw new ArgumentException($"Payload length {payload.Length} does not match fixed frame length {_length}.");
        return payload.ToArray();
    }

    public bool TryExtract(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> message, out int consumed)
    {
        message = default;
        consumed = 0;
        if (buffer.Length < _length) return false;
        message = buffer[.._length];
        consumed = _length;
        return true;
    }
}
