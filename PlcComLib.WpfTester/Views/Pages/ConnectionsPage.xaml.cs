using PlcComLib.WpfTester.Services;
using PlcComLib.WpfTester.ViewModels;
using PlcComLib.WpfTester.Views.Dialogs;
using Wpf.Ui.Controls;

namespace PlcComLib.WpfTester.Views.Pages;

public partial class ConnectionsPage : System.Windows.Controls.Page
{
    private readonly IConnectionManagerService _connectionManager;
    private readonly ITelegramLibraryService _telegramLibrary;

    public ConnectionsPage(ConnectionsPageViewModel viewModel, IConnectionManagerService connectionManager, ITelegramLibraryService telegramLibrary)
    {
        DataContext = viewModel;
        _connectionManager = connectionManager;
        _telegramLibrary = telegramLibrary;
        InitializeComponent();
    }

    private async void OnAddConnectionClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new AddConnectionDialog(_telegramLibrary);
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        if (dialog.ShowDialog() == true && dialog.ResultSettings is not null)
        {
            await _connectionManager.AddConnectionAsync(dialog.ResultSettings);
        }
    }
}
