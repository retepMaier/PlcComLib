using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using PlcComLib.WpfTester.Services;
using PlcComLib.WpfTester.ViewModels;
using PlcComLib.WpfTester.Views;
using PlcComLib.WpfTester.Views.Pages;

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
                services.AddSingleton<INavigationViewPageProvider, NavigationViewPageProvider>();

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<ConnectionsPageViewModel>();
                services.AddSingleton<LogPageViewModel>();

                // Pages
                services.AddSingleton<ConnectionsPage>();
                services.AddSingleton<LogPage>();

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
}
