using Microsoft.Extensions.Logging;
using PlcComLib.WpfTester.Services;

namespace PlcComLib.WpfTester.Services;

public sealed class LogServiceLogger<T>(ILogService logService) : Microsoft.Extensions.Logging.ILogger<T>
{
    private readonly string _source = typeof(T).Name;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (exception is not null)
            message += $"\n{exception}";

        var level = logLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Warning => Models.LogLevel.Warning,
            Microsoft.Extensions.Logging.LogLevel.Error => Models.LogLevel.Error,
            Microsoft.Extensions.Logging.LogLevel.Critical => Models.LogLevel.Error,
            Microsoft.Extensions.Logging.LogLevel.Debug => Models.LogLevel.Debug,
            _ => Models.LogLevel.Info,
        };

        logService.Log(level, _source, message);
    }
}
