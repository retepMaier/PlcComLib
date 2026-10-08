using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace PlcComLib.Udp;

public sealed partial class UdpPlcClient
{
    [LoggerMessage(Level = LogLevel.Information, Message = "UDP client bound to {LocalEndpoint}, sending to {RemoteEndpoint}.")]
    private static partial void LogUdpClientBound(ILogger logger, IPEndPoint? localEndpoint, IPEndPoint remoteEndpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "UDP receive error.")]
    private static partial void LogUdpReceiveError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignoring transient UDP receive error {SocketError}.")]
    private static partial void LogUdpTransientReceiveError(ILogger logger, SocketError socketError);
}
