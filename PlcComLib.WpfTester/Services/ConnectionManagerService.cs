using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using PlcComLib.Tcp;
using PlcComLib.Udp;
using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.ViewModels;

namespace PlcComLib.WpfTester.Services;

public sealed class ConnectionManagerService(ILogService logService) : IConnectionManagerService
{
    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = [];

    public Task AddConnectionAsync(ConnectionSettings settings)
    {
        var logger = new LogServiceLogger<ConnectionManagerService>(logService);
        var vm = new ConnectionItemViewModel(settings, logService, logger);
        Connections.Add(vm);
        return Task.CompletedTask;
    }

    public async Task RemoveConnectionAsync(ConnectionItemViewModel vm)
    {
        await vm.StopAsync();
        Connections.Remove(vm);
    }
}
