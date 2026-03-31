using System.Text.Json;
using System.Text.Json.Serialization;
using PlcComLib.DataTypes;
using Microsoft.Extensions.Logging;

namespace PlcComLib.Telegrams;

/// <summary>
/// Loads and stores TelegramDefinition instances from JSON files.
/// </summary>
public sealed class TelegramRegistry
{
    private readonly Dictionary<string, TelegramDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<TelegramRegistry>? _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TelegramRegistry(ILogger<TelegramRegistry>? logger = null)
    {
        _logger = logger;
    }

    public void LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Telegram definition directory not found: {directory}");
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            LoadFromFile(file);
    }

    public void LoadFromFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            LoadFromJson(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load telegram definition from {File}", filePath);
            throw;
        }
    }

    public void LoadFromJson(string json)
    {
        json = json.TrimStart();
        if (json.StartsWith('['))
        {
            var defs = JsonSerializer.Deserialize<TelegramDefinition[]>(json, JsonOptions)
                       ?? throw new InvalidDataException("Null telegram definition array.");
            foreach (var d in defs)
                Register(d);
        }
        else
        {
            var def = JsonSerializer.Deserialize<TelegramDefinition>(json, JsonOptions)
                      ?? throw new InvalidDataException("Null telegram definition.");
            Register(def);
        }
    }

    public void Register(TelegramDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Id))
            throw new ArgumentException("TelegramDefinition.Id must not be empty.");
        _definitions[definition.Id] = definition;
        _logger?.LogDebug("Registered telegram definition '{Id}' with {FieldCount} fields.", definition.Id, definition.Fields.Count);
    }

    public TelegramDefinition Get(string id)
    {
        if (_definitions.TryGetValue(id, out var def)) return def;
        throw new KeyNotFoundException($"Telegram definition '{id}' not found.");
    }

    public bool TryGet(string id, out TelegramDefinition? definition) =>
        _definitions.TryGetValue(id, out definition);

    public IReadOnlyCollection<TelegramDefinition> Definitions => _definitions.Values;
}
