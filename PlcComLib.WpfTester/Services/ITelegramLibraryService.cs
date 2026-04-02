using System.Collections.ObjectModel;
using PlcComLib.WpfTester.Models;

namespace PlcComLib.WpfTester.Services;

public interface ITelegramLibraryService
{
    ObservableCollection<TelegramDefinitionModel> Telegrams { get; }
    void Add(TelegramDefinitionModel telegram);
    void Remove(TelegramDefinitionModel telegram);
    Task ImportAsync(string filePath);
    Task ExportAsync(string filePath);
}
