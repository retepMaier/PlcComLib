using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.Services;
using Wpf.Ui.Controls;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class ConnectionsPageViewModel : ObservableObject
{
    private readonly IConnectionManagerService _connectionManager;

    public ObservableCollection<ConnectionItemViewModel> Connections => _connectionManager.Connections;

    public ConnectionsPageViewModel(IConnectionManagerService connectionManager)
    {
        _connectionManager = connectionManager;
    }

    [RelayCommand]
    private async Task RemoveConnectionAsync(ConnectionItemViewModel vm)
    {
        await _connectionManager.RemoveConnectionAsync(vm);
    }
}
