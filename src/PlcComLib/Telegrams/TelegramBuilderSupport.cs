namespace PlcComLib.Telegrams;

/// <summary>Validation and offset helpers shared by the TCP/UDP connection builders.</summary>
internal static class TelegramBuilderSupport
{
    /// <summary>
    /// Returns the byte offset of field <paramref name="fieldName"/> in <paramref name="definition"/>:
    /// its PLC-aligned offset for typed telegrams, or the sum of the preceding field sizes for
    /// hand-crafted definitions.
    /// </summary>
    public static int GetFieldOffset(TelegramDefinition definition, string fieldName, string paramName)
    {
        int offset = 0;
        foreach (var field in definition.Fields)
        {
            if (field.Name == fieldName)
                return definition.UsesPlcLayout ? field.PlcOffset : offset;
            offset += field.WireSize;
        }

        throw new ArgumentException(
            $"Property '{fieldName}' was not found in the definition for '{definition.Id}'.", paramName);
    }

    /// <summary>
    /// Validates the length passed to <c>WithLength</c>. For typed telegrams it must equal the
    /// PLC-aligned wire size, otherwise frames would be cut at the wrong position.
    /// </summary>
    public static int CheckLength(TelegramDefinition definition, long length)
    {
        if (length <= 0 || length > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Telegram length must be a positive number of bytes.");

        if (definition.UsesPlcLayout && length != definition.LayoutWireSize)
            throw new ArgumentOutOfRangeException(nameof(length), length,
                $"Telegram '{definition.Id}' is {definition.LayoutWireSize} bytes on the wire " +
                $"(PLC layout including alignment padding), so WithLength must be {definition.LayoutWireSize}. " +
                $"Use {definition.Id}.WireSize.");

        return (int)length;
    }
}
