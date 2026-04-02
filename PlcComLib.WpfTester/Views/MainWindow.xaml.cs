using PlcComLib.WpfTester.Services;
using PlcComLib.WpfTester.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions;

namespace PlcComLib.WpfTester.Views;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationViewPageProvider pageProvider)
    {
        DataContext = viewModel;
        InitializeComponent();
        RootNavigationView.SetPageProviderService(pageProvider);
        Loaded += (_, _) =>
        {
            RootNavigationView.Navigate(typeof(Pages.ConnectionsPage));
        };
    }
}
