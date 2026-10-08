using System.Net;
using Microsoft.Extensions.Logging;

namespace PlcComLib.Tcp;

public sealed partial class TcpPlcServer
{
    [LoggerMessage(Level = LogLevel.Information, Message = "TCP server listening on {Host}:{Port}")]
    private static partial void LogServerListening(ILogger logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Error, Message = "Accept error.")]
    private static partial void LogAcceptError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {Id} connected from {Endpoint}.")]
    private static partial void LogClientConnected(ILogger logger, Guid id, EndPoint? endpoint);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in client handler for {Id}.")]
    private static partial void LogUnhandledClientException(ILogger logger, Exception? ex, Guid id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {Id} disconnected.")]
    private static partial void LogClientDisconnected(ILogger logger, Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unrecognised telegram ID in receive buffer; discarding {Count} byte(s).")]
    private static partial void LogUnrecognisedTelegramId(ILogger logger, int count);

}
