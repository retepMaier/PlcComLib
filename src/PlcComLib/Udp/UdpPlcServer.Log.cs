using Microsoft.Extensions.Logging;

namespace PlcComLib.Udp;

public sealed partial class UdpPlcServer
{
    [LoggerMessage(Level = LogLevel.Information, Message = "UDP server listening on {Host}:{Port}")]
    private static partial void LogServerListening(ILogger? logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Typed handler for {TypeName} threw.")]
    private static partial void LogTypedHandlerThrew(ILogger? logger, Exception ex, string typeName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "UDP receive error.")]
    private static partial void LogUdpReceiveError(ILogger? logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No matching UDP telegram definition for payload of {Length} bytes.")]
    private static partial void LogNoMatchingUdpDefinition(ILogger? logger, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "UDP telegram '{Id}': length field at offset {Offset} extends beyond payload ({PayloadLen} bytes).")]
    private static partial void LogUdpLengthFieldBeyondPayload(ILogger? logger, string id, int offset, int payloadLen);

    [LoggerMessage(Level = LogLevel.Warning, Message = "UDP telegram '{Id}': length field mismatch \u2014 expected {Expected}, got {Received}.")]
    private static partial void LogUdpLengthFieldMismatch(ILogger? logger, string id, long expected, long received);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize UDP telegram '{Id}'.")]
    private static partial void LogUdpDeserializeFailed(ILogger? logger, Exception ex, string id);
}
