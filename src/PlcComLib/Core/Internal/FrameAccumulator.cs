using PlcComLib.Framing;

namespace PlcComLib.Core.Internal;

/// <summary>
/// Collects bytes read from a stream and splits them into frames with an <see cref="IMessageFramer"/>.
/// Unrecognised bytes are skipped by advancing a read position; the buffer is compacted once per
/// <see cref="Drain"/> call instead of once per discarded byte.
/// </summary>
internal sealed class FrameAccumulator(IMessageFramer framer, int maxBufferedBytes)
{
    private byte[] _buffer = new byte[4096];
    private int _length;

    /// <summary>Number of bytes waiting for a complete frame.</summary>
    public int Length => _length;

    public void Append(ReadOnlySpan<byte> data)
    {
        int required = _length + data.Length;
        if (required > maxBufferedBytes)
            throw new InvalidDataException(
                $"Receive buffer would hold {required} bytes without a complete frame (limit {maxBufferedBytes}).");

        if (required > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(required, _buffer.Length * 2));

        data.CopyTo(_buffer.AsSpan(_length));
        _length = required;
    }

    /// <summary>
    /// Extracts every complete frame currently buffered and passes each one to <paramref name="onMessage"/>.
    /// </summary>
    /// <returns>The number of unrecognised bytes that were discarded.</returns>
    public int Drain(Action<byte[]> onMessage)
    {
        int position  = 0;
        int discarded = 0;

        while (position < _length)
        {
            var data = _buffer.AsSpan(position, _length - position);
            if (framer.TryExtract(data, out var message, out int consumed))
            {
                if (consumed <= 0)
                    throw new InvalidOperationException($"{framer.GetType().Name} returned a frame without consuming any bytes.");
                var frame = message.ToArray();
                position += consumed;
                onMessage(frame);
                continue;
            }

            if (consumed <= 0) break; // Wait for more data.
            position  += consumed;
            discarded += consumed;
        }

        if (position > 0)
        {
            _length -= position;
            if (_length > 0) Buffer.BlockCopy(_buffer, position, _buffer, 0, _length);
        }

        return discarded;
    }
}
