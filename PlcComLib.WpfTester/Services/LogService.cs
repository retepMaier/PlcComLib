using System.Collections.ObjectModel;
using System.Windows;
using PlcComLib.WpfTester.Models;

namespace PlcComLib.WpfTester.Services;

public sealed class LogService : ILogService
{
    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Log(LogLevel level, string source, string message)
    {
        var entry = new LogEntry { Level = level, Source = source, Message = message };
        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Entries.Add(entry);
        else
            Application.Current?.Dispatcher.InvokeAsync(() => Entries.Add(entry));
    }

    public void Clear()
    {
        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Entries.Clear();
        else
            Application.Current?.Dispatcher.InvokeAsync(Entries.Clear);
    }
}
