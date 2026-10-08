using System.Net.Sockets;

namespace PlcComLib.Udp;

/// <summary>Socket helpers shared by <see cref="UdpPlcClient"/> and <see cref="UdpPlcServer"/>.</summary>
internal static class UdpSocket
{
    // WSAIoctl SIO_UDP_CONNRESET (_WSAIOW(IOC_VENDOR, 12)).
    private const int SioUdpConnReset = -1744830452;

    /// <summary>
    /// On Windows, an ICMP "port unreachable" reply to an earlier send (e.g. the PLC is offline)
    /// makes the next receive fail with <see cref="SocketError.ConnectionReset"/>. Turn that off.
    /// </summary>
    public static void DisableConnectionReset(Socket socket)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null); }
        catch (SocketException) { /* not supported — receive loop still tolerates ConnectionReset */ }
    }

    /// <summary>
    /// Receive errors that only concern a single datagram or an earlier send; the socket itself
    /// is still usable and the receive loop should continue.
    /// </summary>
    public static bool IsTransient(SocketException ex) => ex.SocketErrorCode is
        SocketError.ConnectionReset or      // ICMP port unreachable for an earlier send (Windows)
        SocketError.ConnectionRefused or    // same, reported by some Linux kernels
        SocketError.MessageSize or          // datagram larger than the receive buffer (Windows)
        SocketError.NetworkReset or
        SocketError.HostUnreachable or
        SocketError.NetworkUnreachable;
}
