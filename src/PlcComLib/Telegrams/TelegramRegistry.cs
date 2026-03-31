namespace PlcComLib.Telegrams;

/// <summary>
/// Stores <see cref="TelegramDefinition"/> instances registered programmatically
/// (or produced by the source generator via <c>T.Definition</c>).
/// </summary>
public sealed class TelegramRegistry
{
    private readonly Dictionary<string, TelegramDefinition> _definitions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a telegram definition. Overwrites any existing entry with the same Id.</summary>
    public void Register(TelegramDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Id))
            throw new ArgumentException("TelegramDefinition.Id must not be empty.");
        _definitions[definition.Id] = definition;
    }

    /// <summary>Returns the definition for <paramref name="id"/> or throws.</summary>
    public TelegramDefinition Get(string id)
    {
        if (_definitions.TryGetValue(id, out var def)) return def;
        throw new KeyNotFoundException($"Telegram definition '{id}' not found.");
    }

    /// <summary>Tries to get the definition for <paramref name="id"/>.</summary>
    public bool TryGet(string id, out TelegramDefinition? definition) =>
        _definitions.TryGetValue(id, out definition);

    /// <summary>All registered definitions.</summary>
    public IReadOnlyCollection<TelegramDefinition> Definitions => _definitions.Values;
}
