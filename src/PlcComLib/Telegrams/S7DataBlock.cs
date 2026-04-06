using PlcComLib.DataTypes;

namespace PlcComLib.Telegrams;

/// <summary>
/// Represents a serialised S7 Data Block: the raw byte array together with
/// named, PLC-offset-indexed field descriptors.
/// Returned by the <c>.ToDataBlock()</c> extension method.
/// </summary>
public sealed class S7DataBlock
{
    private readonly byte[] _bytes;
    private readonly IReadOnlyList<TelegramField> _fields;
    private readonly Dictionary<string, TelegramField> _byName;

    internal S7DataBlock(byte[] bytes, IReadOnlyList<TelegramField> fields)
    {
        _bytes  = bytes;
        _fields = fields;
        _byName = fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
    }

    /// <summary>Total byte length of the data block (PLC-aligned).</summary>
    public int Length => _bytes.Length;

    /// <summary>The raw serialised bytes of the data block.</summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>All field descriptors, in declaration order.</summary>
    public IReadOnlyList<TelegramField> Fields => _fields;

    /// <summary>
    /// Returns the raw bytes for the field with the given name,
    /// sliced from the data block at the field's <see cref="TelegramField.PlcOffset"/>.
    /// </summary>
    public ReadOnlySpan<byte> this[string fieldName]
    {
        get
        {
            if (!_byName.TryGetValue(fieldName, out var field))
                throw new KeyNotFoundException($"Field '{fieldName}' not found in data block.");
            // Bool fields share a byte; return the whole byte for context
            int size = field.DataType == S7DataType.Bool ? 1 : field.WireSize;
            return _bytes.AsSpan(field.PlcOffset, size);
        }
    }

    /// <summary>
    /// Returns the byte offset of the named field as the PLC would address it.
    /// </summary>
    public int OffsetOf(string fieldName)
    {
        if (!_byName.TryGetValue(fieldName, out var field))
            throw new KeyNotFoundException($"Field '{fieldName}' not found in data block.");
        return field.PlcOffset;
    }

    /// <summary>
    /// Returns a copy of the raw bytes as a <c>byte[]</c>.
    /// </summary>
    public byte[] ToArray() => _bytes.ToArray();

    /// <summary>
    /// Returns a hex dump string for diagnostic purposes.
    /// Format: "00 1A FF 3C …"
    /// </summary>
    public override string ToString()
        => BitConverter.ToString(_bytes).Replace('-', ' ');
}
