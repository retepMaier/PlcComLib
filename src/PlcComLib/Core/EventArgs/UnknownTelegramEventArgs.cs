using PlcComLib.DataTypes;

namespace PlcComLib.Core.Events;

/// <summary>
/// Event args raised when a received payload cannot be matched to any registered telegram definition.
/// </summary>
public sealed class UnknownTelegramEventArgs : EventArgs
{
    public UnknownTelegramEventArgs(byte[] payload, ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Payload   = payload;
        ByteOrder = byteOrder;
    }

    /// <summary>
    /// The raw wire bytes of the unrecognised message (after any framing has been stripped).
    /// </summary>
    public byte[] Payload { get; }

    /// <summary>
    /// The byte order used by the connection that received this payload.
    /// </summary>
    public ByteOrder ByteOrder { get; }

    /// <summary>
    /// The first two bytes of <see cref="Payload"/> interpreted as a <c>ushort</c>
    /// using the connection's <see cref="ByteOrder"/>, or <c>0</c> if the payload is shorter than 2 bytes.
    /// </summary>
    public ushort CandidateTelegramId =>
        Payload.Length >= 2
            ? (ByteOrder == ByteOrder.LittleEndian
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(Payload)
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(Payload))
            : (ushort)0;
}
