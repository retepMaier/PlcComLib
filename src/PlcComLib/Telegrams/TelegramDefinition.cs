namespace PlcComLib.Telegrams;

/// <summary>
/// Defines a telegram: its identity, optional description, and ordered list of fields.
/// </summary>
public sealed class TelegramDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// 2-byte big-endian MessageId used for typed-telegram dispatch.
    /// <c>0</c> means not set (legacy untyped definitions still work).
    /// </summary>
    public ushort MessageId { get; set; }

    public List<TelegramField> Fields { get; set; } = [];
    public int TotalWireSize => Fields.Sum(f => f.WireSize);
}
