using Microsoft.Extensions.Logging;

namespace PlcComLib.Core.Internal;

/// <summary>
/// Raises events so that an exception thrown by one subscriber is logged instead of
/// escaping into a receive, accept or reconnect loop (and stopping it), and does not
/// prevent the remaining subscribers from being called.
/// </summary>
internal static partial class EventRaiser
{
    public static void Raise<TArgs>(EventHandler<TArgs>? handler, object sender, TArgs args, ILogger logger)
    {
        if (handler is null) return;

        if (handler.HasSingleTarget)
        {
            Invoke(handler, sender, args, logger);
            return;
        }

        foreach (var single in Delegate.EnumerateInvocationList(handler))
            Invoke(single, sender, args, logger);
    }

    private static void Invoke<TArgs>(EventHandler<TArgs> handler, object sender, TArgs args, ILogger logger)
    {
        try { handler(sender, args); }
        catch (Exception ex) { LogEventHandlerThrew(logger, ex, typeof(TArgs).Name); }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "An event handler for {EventArgsType} threw an exception.")]
    private static partial void LogEventHandlerThrew(ILogger logger, Exception ex, string eventArgsType);
}
