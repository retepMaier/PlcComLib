namespace PlcComLib.Telegrams;

/// <summary>
/// JSON-deserializable definition of a telegram.
/// </summary>
public sealed class TelegramDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<TelegramField> Fields { get; set; } = new();
    public int TotalWireSize => Fields.Sum(f => f.WireSize);
}
