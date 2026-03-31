namespace PlcComLib.Framing;

/// <summary>
/// Abstracts message framing: splitting a byte stream into discrete messages.
/// </summary>
public interface IMessageFramer
{
    byte[] Frame(ReadOnlySpan<byte> payload);
    bool TryExtract(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> message, out int consumed);
}
