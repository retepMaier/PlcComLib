using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.ViewModels;
using System.Collections.ObjectModel;

namespace PlcComLib.WpfTester.Services;

public sealed class ConnectionManagerService(ILogService logService, ITelegramLibraryService telegramLibrary) : IConnectionManagerService
{
    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = [];

    public Task AddConnectionAsync(ConnectionSettings settings)
    {
        var logger = new LogServiceLogger<ConnectionManagerService>(logService);
        var vm = new ConnectionItemViewModel(settings, logService, telegramLibrary, logger);
        Connections.Add(vm);
        return Task.CompletedTask;
    }

    public async Task RemoveConnectionAsync(ConnectionItemViewModel vm)
    {
        await vm.StopAsync();
        Connections.Remove(vm);
    }
}
