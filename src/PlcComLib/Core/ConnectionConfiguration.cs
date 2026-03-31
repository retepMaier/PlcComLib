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
}
