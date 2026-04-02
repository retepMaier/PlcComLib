using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.Services;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class LogPageViewModel : ObservableObject
{
    private readonly ILogService _logService;

    public ObservableCollection<LogEntry> Entries => _logService.Entries;

    [ObservableProperty]
    private bool _autoScroll = true;

    public LogPageViewModel(ILogService logService)
    {
        _logService = logService;
    }

    [RelayCommand]
    private void Clear() => _logService.Clear();
}
