namespace PlcComLib.Core;

/// <summary>
/// Defines whether the connection acts as a client or server.
/// </summary>
public enum ConnectionMode
{
    /// <summary>The application initiates the connection (connects to the PLC).</summary>
    Client,

    /// <summary>The application listens for incoming connections from the PLC.</summary>
    Server
}
