using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlcComLib.WpfTester.Models;

namespace PlcComLib.WpfTester.Services;

public sealed class TelegramLibraryService : ITelegramLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ObservableCollection<TelegramDefinitionModel> Telegrams { get; } = [];

    public void Add(TelegramDefinitionModel telegram) => Telegrams.Add(telegram);

    public void Remove(TelegramDefinitionModel telegram) => Telegrams.Remove(telegram);

    public async Task ImportAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var list = JsonSerializer.Deserialize<List<TelegramDefinitionModel>>(json, JsonOptions);
        if (list is null) return;
        foreach (var t in list)
            Telegrams.Add(t);
    }

    public async Task ExportAsync(string filePath)
    {
        var json = JsonSerializer.Serialize(Telegrams.ToList(), JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }
}
