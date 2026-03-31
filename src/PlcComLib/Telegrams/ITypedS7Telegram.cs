namespace PlcComLib.Telegrams;

/// <summary>
/// Implemented by source-generated typed telegram classes.
/// Uses static abstract members for zero-reflection dispatch (.NET 10+).
/// </summary>
/// <typeparam name="TSelf">The concrete telegram type (CRTP).</typeparam>
public interface ITypedS7Telegram<TSelf> where TSelf : ITypedS7Telegram<TSelf>
{
    /// <summary>The 2-byte big-endian message identifier prepended to the wire payload.</summary>
    static abstract ushort MessageId { get; }

    /// <summary>Total wire size in bytes, including the 2-byte MessageId header.</summary>
    static abstract int WireSize { get; }

    /// <summary>The telegram definition built from the declared attributes (no JSON required).</summary>
    static abstract TelegramDefinition Definition { get; }

    /// <summary>Deserialises a telegram from a raw wire payload. Validates MessageId and buffer length.</summary>
    static abstract TSelf Deserialize(ReadOnlySpan<byte> data);

    /// <summary>Serialises this telegram to its wire payload.</summary>
    byte[] Serialize();
}
