using System.Net;
using Microsoft.Extensions.Logging;

namespace PlcComLib.Tcp;

public sealed partial class TcpPlcServer
{
    [LoggerMessage(Level = LogLevel.Information, Message = "TCP server listening on {Host}:{Port}")]
    private static partial void LogServerListening(ILogger? logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Typed handler for {TypeName} threw.")]
    private static partial void LogTypedHandlerThrew(ILogger? logger, Exception ex, string typeName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Accept error.")]
    private static partial void LogAcceptError(ILogger? logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {Id} connected from {Endpoint}.")]
    private static partial void LogClientConnected(ILogger? logger, Guid id, EndPoint? endpoint);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in client handler for {Id}.")]
    private static partial void LogUnhandledClientException(ILogger? logger, Exception? ex, Guid id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {Id} disconnected.")]
    private static partial void LogClientDisconnected(ILogger? logger, Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unrecognised telegram ID in receive buffer; discarding {Count} byte(s).")]
    private static partial void LogUnrecognisedTelegramId(ILogger? logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No matching telegram definition for payload of {Length} bytes from client {ClientId}.")]
    private static partial void LogNoMatchingDefinitionFromClient(ILogger? logger, int length, Guid clientId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram '{Id}' from client {ClientId}: length field at offset {Offset} extends beyond payload ({PayloadLen} bytes).")]
    private static partial void LogLengthFieldBeyondPayloadFromClient(ILogger? logger, string id, Guid clientId, int offset, int payloadLen);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram '{Id}' from client {ClientId}: length field mismatch \u2014 expected {Expected}, got {Received}.")]
    private static partial void LogLengthFieldMismatchFromClient(ILogger? logger, string id, Guid clientId, long expected, long received);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize telegram '{Id}' from client {ClientId}.")]
    private static partial void LogDeserializeFailedFromClient(ILogger? logger, Exception ex, string id, Guid clientId);
}
