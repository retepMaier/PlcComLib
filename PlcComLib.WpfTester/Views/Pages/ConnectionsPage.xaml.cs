using PlcComLib.WpfTester.Services;
using PlcComLib.WpfTester.ViewModels;
using PlcComLib.WpfTester.Views.Dialogs;
using Wpf.Ui.Controls;

namespace PlcComLib.WpfTester.Views.Pages;

public partial class ConnectionsPage : System.Windows.Controls.Page
{
    private readonly IConnectionManagerService _connectionManager;
    private readonly IServiceProvider _serviceProvider;

    public ConnectionsPage(ConnectionsPageViewModel viewModel, IConnectionManagerService connectionManager, IServiceProvider serviceProvider)
    {
        DataContext = viewModel;
        _connectionManager = connectionManager;
        _serviceProvider = serviceProvider;
        InitializeComponent();
    }

    private async void OnAddConnectionClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new AddConnectionDialog();
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        if (dialog.ShowDialog() == true && dialog.ResultSettings is not null)
        {
            await _connectionManager.AddConnectionAsync(dialog.ResultSettings);
        }
    }
}
