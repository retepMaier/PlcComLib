namespace PlcComLib.DataTypes;

/// <summary>Describes a single field for S7 layout computation.</summary>
public readonly record struct S7FieldDescriptor(string Name, S7DataType DataType, int WireSize);

/// <summary>The computed PLC-aligned layout for a single field.</summary>
public readonly record struct S7FieldLayout(
    string Name,
    S7DataType DataType,
    int PlcOffset,
    int WireSize,
    int PaddingBefore,
    int BitIndex);

/// <summary>The result of computing the S7 PLC-aligned layout for a sequence of fields.</summary>
public readonly record struct S7LayoutResult(
    IReadOnlyList<S7FieldLayout> Fields,
    int TotalPlcSize,
    int TotalLibrarySize,
    bool HasPadding);

/// <summary>
/// Computes S7 PLC-accurate byte offsets for a sequence of field descriptors,
/// applying the two mandatory S7 DB layout rules:
/// <list type="bullet">
///   <item><description>Rule 1 — "Even Byte": any field wider than 1 byte must start at an even offset.</description></item>
///   <item><description>Rule 2 — "Bit-Packing": consecutive Bool fields share a byte (up to 8 per byte).</description></item>
/// </list>
/// </summary>
public static class S7Layout
{
    /// <summary>
    /// Computes the PLC-aligned layout for the given field descriptors.
    /// </summary>
    public static S7LayoutResult Compute(IEnumerable<S7FieldDescriptor> fields)
    {
        var fieldList = fields as IReadOnlyList<S7FieldDescriptor> ?? [.. fields];
        var result    = new List<S7FieldLayout>(fieldList.Count);

        int offset     = 0;
        bool inBoolByte = false;
        int boolBitPos  = 0;
        int boolOffset  = 0; // byte offset where the current bool-byte lives

        foreach (var field in fieldList)
        {
            switch (field.DataType)
            {
                case S7DataType.Bool:
                {
                    if (!inBoolByte)
                    {
                        // Open a new bool byte at the current offset
                        boolOffset  = offset;
                        inBoolByte  = true;
                        boolBitPos  = 0;
                    }
                    result.Add(new S7FieldLayout(
                        Name:          field.Name,
                        DataType:      field.DataType,
                        PlcOffset:     boolOffset,
                        WireSize:      field.WireSize,
                        PaddingBefore: 0,
                        BitIndex:      boolBitPos));
                    boolBitPos++;
                    if (boolBitPos == 8)
                    {
                        // Bool byte is full — close it
                        inBoolByte = false;
                        offset++;
                    }
                    break;
                }

                case S7DataType.Byte:
                case S7DataType.USInt:
                case S7DataType.SInt:
                case S7DataType.Char:
                {
                    if (inBoolByte) { inBoolByte = false; offset++; }
                    result.Add(new S7FieldLayout(
                        Name:          field.Name,
                        DataType:      field.DataType,
                        PlcOffset:     offset,
                        WireSize:      field.WireSize,
                        PaddingBefore: 0,
                        BitIndex:      -1));
                    offset += 1;
                    break;
                }

                case S7DataType.Raw:
                case S7DataType.CharArray:
                {
                    // Byte arrays: 1-byte aligned, no even-byte padding needed
                    if (inBoolByte) { inBoolByte = false; offset++; }
                    result.Add(new S7FieldLayout(
                        Name:          field.Name,
                        DataType:      field.DataType,
                        PlcOffset:     offset,
                        WireSize:      field.WireSize,
                        PaddingBefore: 0,
                        BitIndex:      -1));
                    offset += field.WireSize;
                    break;
                }

                default:
                {
                    // Multi-byte types: WireSize >= 2 — apply even-byte rule
                    if (inBoolByte) { inBoolByte = false; offset++; }
                    int padding = offset % 2 != 0 ? 1 : 0;
                    offset += padding;
                    result.Add(new S7FieldLayout(
                        Name:          field.Name,
                        DataType:      field.DataType,
                        PlcOffset:     offset,
                        WireSize:      field.WireSize,
                        PaddingBefore: padding,
                        BitIndex:      -1));
                    offset += field.WireSize;
                    break;
                }
            }
        }

        // Close any still-open bool byte
        if (inBoolByte) offset++;

        int totalLibrarySize = 0;
        foreach (var f in fieldList) totalLibrarySize += f.WireSize;

        bool hasPadding = offset != totalLibrarySize ||
                          result.Any(static f => f.PaddingBefore > 0);

        return new S7LayoutResult(
            Fields:           result,
            TotalPlcSize:     offset,
            TotalLibrarySize: totalLibrarySize,
            HasPadding:       hasPadding);
    }
}
