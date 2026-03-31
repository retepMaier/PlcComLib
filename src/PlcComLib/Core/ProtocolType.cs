namespace PlcComLib.Core;

/// <summary>
/// Network transport protocol.
/// </summary>
public enum ProtocolType
{
    /// <summary>TCP/IP – reliable, connection-oriented.</summary>
    Tcp,

    /// <summary>UDP/IP – connectionless datagram.</summary>
    Udp
}
