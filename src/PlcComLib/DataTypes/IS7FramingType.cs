using PlcComLib.Core.PlcTypes;

namespace PlcComLib.DataTypes;

/// <summary>
/// Marker interface implemented by every S7 value struct (<see cref="S7Word"/>, <see cref="S7Int"/>, …).
/// Use as a type argument to <c>.WithMessageId&lt;S7Word&gt;</c> / <c>.WithLength&lt;S7Int&gt;</c>
/// on connection builders, or declare telegram properties with the concrete struct types so that
/// <see cref="PlcComLib.Telegrams.S7TelegramBase{TSelf}"/> can serialise them automatically.
/// </summary>
public interface IS7FramingType
{
    /// <summary>The S7 data type represented by this struct.</summary>
    static abstract S7DataType DataType { get; }

    /// <summary>Number of bytes this type occupies on the wire.</summary>
    static abstract int WireSize { get; }
}
