using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.Services;
using Wpf.Ui.Controls;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class ConnectionsPageViewModel(IConnectionManagerService connectionManager) : ObservableObject
{
    public ObservableCollection<ConnectionItemViewModel> Connections => connectionManager.Connections;

    [RelayCommand]
    private async Task RemoveConnectionAsync(ConnectionItemViewModel vm)
    {
        await connectionManager.RemoveConnectionAsync(vm);
    }
}
