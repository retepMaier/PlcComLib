using PlcComLib.DataTypes;

namespace PlcComLib.Telegrams;

/// <summary>
/// Implemented by source-generated typed telegram classes.
/// Uses static abstract members for zero-reflection dispatch (.NET 10+).
/// Extends <see cref="ITelegram"/> so that only telegrams carrying a <c>TelegramId</c>
/// can be sent or subscribed to via client/server instances.
/// </summary>
/// <typeparam name="TSelf">The concrete telegram type (CRTP).</typeparam>
public interface ITypedS7Telegram<TSelf> : ITelegram where TSelf : ITypedS7Telegram<TSelf>
{
    /// <summary>The 2-byte message identifier prepended to the wire payload.</summary>
    new static abstract ushort MessageId { get; }

    /// <summary>Total wire size in bytes, including the 2-byte MessageId header.</summary>
    static abstract int WireSize { get; }

    /// <summary>The telegram definition built from the declared attributes (no JSON required).</summary>
    static abstract TelegramDefinition Definition { get; }

    /// <summary>
    /// Deserialises a telegram from a raw wire payload.
    /// Validates MessageId and buffer length.
    /// </summary>
    /// <param name="data">Raw wire bytes (including the 2-byte MessageId header).</param>
    /// <param name="byteOrder">
    /// Byte order for multi-byte data fields and the MessageId header. Supplied by the client/server connection.
    /// </param>
    static abstract TSelf Deserialize(ReadOnlySpan<byte> data,
        ByteOrder byteOrder = ByteOrder.BigEndian);

    /// <summary>
    /// Serialises this telegram to its wire payload.
    /// </summary>
    /// <param name="byteOrder">
    /// Byte order for multi-byte data fields and the MessageId header. Supplied by the client/server connection.
    /// </param>
    byte[] Serialize(ByteOrder byteOrder = ByteOrder.BigEndian);
}
