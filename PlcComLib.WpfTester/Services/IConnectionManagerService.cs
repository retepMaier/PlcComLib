using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.ViewModels;

namespace PlcComLib.WpfTester.Services;

public interface IConnectionManagerService
{
    System.Collections.ObjectModel.ObservableCollection<ConnectionItemViewModel> Connections { get; }
    Task AddConnectionAsync(ConnectionSettings settings);
    Task RemoveConnectionAsync(ConnectionItemViewModel vm);
}
