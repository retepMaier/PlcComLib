namespace PlcComLib.Core.Events;

/// <summary>Event args carrying raw bytes from the wire, used for diagnostic events.</summary>
public sealed class RawBytesEventArgs : EventArgs
{
    public RawBytesEventArgs(byte[] data, string remoteAddress = "", int port = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data          = data;
        RemoteAddress = remoteAddress;
        Port          = port;
    }

    /// <summary>The raw bytes transferred over the network.</summary>
    public byte[] Data { get; }

    /// <summary>The IP address of the remote endpoint (destination for sent bytes, source for received bytes).</summary>
    public string RemoteAddress { get; }

    /// <summary>The port of the remote endpoint (destination for sent bytes, source for received bytes).</summary>
    public int Port { get; }
}
