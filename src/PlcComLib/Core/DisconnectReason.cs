namespace PlcComLib.Core;

/// <summary>
/// Describes why a connection was closed or why a connection attempt failed.
/// </summary>
public enum DisconnectReason
{
    /// <summary>Not disconnected (used when the connection was just established).</summary>
    None,

    /// <summary><see cref="IPlcConnection.StopAsync"/> was called deliberately.</summary>
    Stopped,

    /// <summary>The remote endpoint closed the connection gracefully (read returned 0 bytes).</summary>
    RemoteClose,

    /// <summary>A socket read or write error occurred on an established connection.</summary>
    IoError,

    /// <summary>The TCP client could not reach the remote endpoint during a connect attempt.</summary>
    ConnectFailed,

    /// <summary>The connection object was disposed.</summary>
    Disposed
}
