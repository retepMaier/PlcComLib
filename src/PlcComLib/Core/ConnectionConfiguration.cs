namespace PlcComLib.Core;

/// <summary>
/// Configuration for a PLC communication endpoint.
/// </summary>
public sealed class ConnectionConfiguration
{
    /// <summary>IP address or hostname of the remote PLC (client mode) or local bind address (server mode).</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>Port number.</summary>
    public int Port { get; init; } = 2000;

    /// <summary>Client or Server.</summary>
    public ConnectionMode Mode { get; init; } = ConnectionMode.Client;

    /// <summary>Transport protocol.</summary>
    public ProtocolType Protocol { get; init; } = ProtocolType.Tcp;

    /// <summary>Milliseconds to wait between reconnection attempts (TCP client only).</summary>
    public int ReconnectIntervalMs { get; init; } = 5000;

    /// <summary>Send/receive timeout in milliseconds.</summary>
    public int TimeoutMs { get; init; } = 10000;

    /// <summary>Maximum number of concurrent client connections (TCP server only).</summary>
    public int MaxConnections { get; init; } = 10;

    /// <summary>
    /// When <c>true</c>, disables Nagle's algorithm (TCP_NODELAY) on the socket.
    /// Recommended for low-latency PLC communication. Default: <c>false</c>.
    /// Only applies to TCP connections.
    /// </summary>
    public bool NoDelay { get; init; } = false;

    /// <summary>
    /// Socket receive buffer size in bytes. <c>0</c> leaves the OS default unchanged.
    /// Increasing this value reduces packet loss under burst load.
    /// </summary>
    public int ReceiveBufferSize { get; init; } = 0;

    /// <summary>
    /// Socket send buffer size in bytes. <c>0</c> leaves the OS default unchanged.
    /// Increasing this value improves throughput when sending large amounts of data.
    /// </summary>
    public int SendBufferSize { get; init; } = 0;
}
