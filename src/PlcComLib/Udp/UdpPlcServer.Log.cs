using Microsoft.Extensions.Logging;

namespace PlcComLib.Udp;

public sealed partial class UdpPlcServer
{
    [LoggerMessage(Level = LogLevel.Information, Message = "UDP server listening on {Host}:{Port}")]
    private static partial void LogServerListening(ILogger logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "UDP receive error.")]
    private static partial void LogUdpReceiveError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignoring transient UDP receive error {SocketError}.")]
    private static partial void LogUdpTransientReceiveError(ILogger logger, System.Net.Sockets.SocketError socketError);
}
