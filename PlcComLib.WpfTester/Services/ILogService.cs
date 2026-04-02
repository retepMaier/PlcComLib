using PlcComLib.WpfTester.Models;

namespace PlcComLib.WpfTester.Services;

public interface ILogService
{
    System.Collections.ObjectModel.ObservableCollection<LogEntry> Entries { get; }
    void Log(LogLevel level, string source, string message);
    void Clear();
}
