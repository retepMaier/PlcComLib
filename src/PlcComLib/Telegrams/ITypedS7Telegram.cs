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
    /// <summary>
    /// The telegram definition built from the declared attributes (no JSON required).
    /// The <c>MessageId</c> on this definition is set at registration time via
    /// <c>RegisterTelegram&lt;T&gt;().WithMessageId(id)</c> on the connection builder.
    /// </summary>
    static abstract TelegramDefinition Definition { get; }

    /// <summary>
    /// Deserialises a telegram from a raw wire payload.
    /// Validates buffer length against <c>WireSize</c>.
    /// </summary>
    /// <param name="data">Raw wire bytes (exactly the user-declared data fields).</param>
    /// <param name="byteOrder">
    /// Byte order for multi-byte data fields. Supplied by the client/server connection.
    /// </param>
    static abstract TSelf Deserialize(ReadOnlySpan<byte> data,ByteOrder byteOrder = ByteOrder.BigEndian);

    /// <summary>
    /// Serialises this telegram to its wire payload (exactly the user-declared data fields).
    /// </summary>
    /// <param name="byteOrder">
    /// Byte order for multi-byte data fields. Supplied by the client/server connection.
    /// </param>
    byte[] Serialize(ByteOrder byteOrder = ByteOrder.BigEndian);
}
