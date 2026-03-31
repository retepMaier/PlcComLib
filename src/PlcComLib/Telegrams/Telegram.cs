namespace PlcComLib.Telegrams;

/// <summary>
/// A runtime telegram instance with typed field values.
/// </summary>
public sealed class Telegram
{
    private readonly Dictionary<string, object> _fields = new(StringComparer.OrdinalIgnoreCase);

    public Telegram(TelegramDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public TelegramDefinition Definition { get; }

    public T GetValue<T>(string fieldName)
    {
        if (!_fields.TryGetValue(fieldName, out var raw))
            throw new KeyNotFoundException($"Field '{fieldName}' not found in telegram '{Definition.Id}'.");
        if (raw is T typed)
            return typed;
        return (T)Convert.ChangeType(raw, typeof(T));
    }

    public object GetValue(string fieldName)
    {
        if (!_fields.TryGetValue(fieldName, out var raw))
            throw new KeyNotFoundException($"Field '{fieldName}' not found in telegram '{Definition.Id}'.");
        return raw;
    }

    public void SetValue(string fieldName, object value)
    {
        _ = Definition.Fields.FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Field '{fieldName}' does not exist in telegram '{Definition.Id}'.");
        _fields[fieldName] = value;
    }

    public IReadOnlyList<string> FieldNames => Definition.Fields.Select(f => f.Name).ToList();
}
