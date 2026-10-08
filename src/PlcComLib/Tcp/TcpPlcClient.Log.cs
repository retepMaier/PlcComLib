using Microsoft.Extensions.Logging;

namespace PlcComLib.Tcp;

public sealed partial class TcpPlcClient
{

    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to {Host}:{Port}...")]
    private static partial void LogConnecting(ILogger logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection to {Host}:{Port} failed. Retrying in {Interval} ms.")]
    private static partial void LogConnectionFailed(ILogger logger, Exception ex, string host, int port, int interval);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Read error on TCP stream.")]
    private static partial void LogTcpReadError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Remote endpoint closed the connection.")]
    private static partial void LogRemoteEndpointClosed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unrecognised telegram ID in receive buffer; discarding {Count} byte(s).")]
    private static partial void LogUnrecognisedTelegramId(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection state: {State} ({Reason})")]
    private static partial void LogConnectionState(ILogger logger, string state, string reason);
}
