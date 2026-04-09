using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlcComLib.WpfTester.Services;
using PlcComLib.WpfTester.ViewModels;
using PlcComLib.WpfTester.Views;
using PlcComLib.WpfTester.Views.Pages;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions;

namespace PlcComLib.WpfTester;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Core services
                services.AddSingleton<ILogService, LogService>();
                services.AddSingleton<IConnectionManagerService, ConnectionManagerService>();
                services.AddSingleton<ITelegramLibraryService, TelegramLibraryService>();
                services.AddSingleton<INavigationViewPageProvider, NavigationViewPageProvider>();

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<ConnectionsPageViewModel>();
                services.AddSingleton<LogPageViewModel>();
                services.AddSingleton<TelegramsPageViewModel>();

                // Pages
                services.AddSingleton<ConnectionsPage>();
                services.AddSingleton<LogPage>();
                services.AddSingleton<TelegramsPage>();

                // Main window
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }


    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Debug.WriteLine(e.Exception);
        MessageBox.Show(
            e.Exception.ToString(),
            "Unhandled UI exception",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
