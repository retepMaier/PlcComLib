namespace PlcComLib.DataTypes;

/// <summary>
/// Marker interface for S7 data types used in framing configuration.
/// Implement via the concrete structs (<see cref="S7Word"/>, <see cref="S7Int"/>, etc.)
/// and pass as a type argument to
/// <c>.WithMessageId&lt;S7Word&gt;(id, byteOffset)</c> or
/// <c>.WithLength&lt;S7Int&gt;(length, byteOffset)</c> on the connection builder.
/// </summary>
public interface IS7FramingType
{
    /// <summary>The S7 data type represented by this framing marker.</summary>
    static abstract S7DataType DataType { get; }
}
