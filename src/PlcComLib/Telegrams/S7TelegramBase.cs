using PlcComLib.DataTypes;

namespace PlcComLib.Telegrams;

/// <summary>
/// Abstract base class for strongly-typed S7 telegrams.
/// Derive from this class and declare properties using the S7 value structs
/// (<see cref="S7Word"/>, <see cref="S7Int"/>, <see cref="S7Real"/>, …).
/// Serialization and deserialization are handled automatically via
/// <see cref="S7TelegramReflector{T}"/> which compiles delegates once per type
/// at first use — no runtime reflection on hot paths, zero boxing of the
/// telegram instance.
/// </summary>
/// <typeparam name="TSelf">The concrete telegram type (CRTP).</typeparam>
/// <example>
/// <code>
/// public class MachineStatus : S7TelegramBase&lt;MachineStatus&gt;
/// {
///     public S7Word  MachineId    { get; set; } = 0;
///     public S7Real  CurrentSpeed { get; set; } = 0f;
///     public S7Bool  IsRunning    { get; set; } = false;
///     public S7String&lt;L10&gt; Label { get; set; } = "";
/// }
/// </code>
/// </example>
public abstract class S7TelegramBase<TSelf> : ITypedS7Telegram<TSelf>
    where TSelf : S7TelegramBase<TSelf>, new()
{
    // ── Static members — satisfy ITypedS7Telegram<TSelf> for all derived classes ──

    /// <summary>
    /// The telegram definition built from the declared S7 properties.
    /// <see cref="TelegramDefinition.MessageId"/> is set at registration time via
    /// <c>.WithMessageId(id)</c> on the connection builder.
    /// </summary>
    public static TelegramDefinition Definition => S7TelegramReflector<TSelf>.Definition;

    /// <summary>Total wire size in bytes (sum of all declared S7 field widths).</summary>
    public static int WireSize => S7TelegramReflector<TSelf>.WireSize;

    /// <summary>
    /// Deserialises a telegram from raw wire bytes.
    /// Delegates to the pre-compiled plan in <see cref="S7TelegramReflector{TSelf}"/>.
    /// </summary>
    public static TSelf Deserialize(
        ReadOnlySpan<byte> data,
        ByteOrder byteOrder = ByteOrder.BigEndian)
        => S7TelegramReflector<TSelf>.Deserialize(data, byteOrder);

    // ── Instance members ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ushort TelegramId => (ushort)S7TelegramReflector<TSelf>.Definition.MessageId;

    /// <summary>
    /// Serialises this telegram to its wire representation.
    /// Delegates to the pre-compiled plan in <see cref="S7TelegramReflector{TSelf}"/>.
    /// </summary>
    public byte[] Serialize(ByteOrder byteOrder = ByteOrder.BigEndian)
        => S7TelegramReflector<TSelf>.Serialize((TSelf)this, byteOrder);

    // ── Explicit interface implementation (avoids ambiguity) ──────────────────
    byte[] ITypedS7Telegram<TSelf>.Serialize(ByteOrder byteOrder) => Serialize(byteOrder);
}
