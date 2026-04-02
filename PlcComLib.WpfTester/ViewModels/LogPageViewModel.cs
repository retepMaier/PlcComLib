using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.Services;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class LogPageViewModel(ILogService logService) : ObservableObject
{
    public ObservableCollection<LogEntry> Entries => logService.Entries;

    [ObservableProperty]
    public partial bool AutoScroll { get; set; } = true;

    [RelayCommand]
    private void Clear() => logService.Clear();
}
