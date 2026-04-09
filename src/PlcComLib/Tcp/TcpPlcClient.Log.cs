using Microsoft.Extensions.Logging;

namespace PlcComLib.Tcp;

public sealed partial class TcpPlcClient
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Typed handler for {TypeName} threw.")]
    private static partial void LogTypedHandlerThrew(ILogger? logger, Exception ex, string typeName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to {Host}:{Port}...")]
    private static partial void LogConnecting(ILogger? logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection to {Host}:{Port} failed. Retrying in {Interval} ms.")]
    private static partial void LogConnectionFailed(ILogger? logger, Exception ex, string host, int port, int interval);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Read error on TCP stream.")]
    private static partial void LogTcpReadError(ILogger? logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Remote endpoint closed the connection.")]
    private static partial void LogRemoteEndpointClosed(ILogger? logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unrecognised telegram ID in receive buffer; discarding {Count} byte(s).")]
    private static partial void LogUnrecognisedTelegramId(ILogger? logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No matching telegram definition for payload of {Length} bytes.")]
    private static partial void LogNoMatchingDefinition(ILogger? logger, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram '{Id}': length field at offset {Offset} extends beyond payload ({PayloadLen} bytes).")]
    private static partial void LogLengthFieldBeyondPayload(ILogger? logger, string id, int offset, int payloadLen);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram '{Id}': length field mismatch \u2014 expected {Expected}, got {Received}.")]
    private static partial void LogLengthFieldMismatch(ILogger? logger, string id, long expected, long received);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize telegram '{Id}'.")]
    private static partial void LogDeserializeFailed(ILogger? logger, Exception ex, string id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection state: {State} ({Reason})")]
    private static partial void LogConnectionState(ILogger? logger, string state, string reason);
}
