using System.Windows;
using Wpf.Ui.Abstractions;

namespace PlcComLib.WpfTester.Services;

public sealed class NavigationViewPageProvider(IServiceProvider serviceProvider) : INavigationViewPageProvider
{
    public object? GetPage(Type pageType) => serviceProvider.GetService(pageType);
}
