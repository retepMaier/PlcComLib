using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.Udp;

/// <summary>
/// Fluent builder for <see cref="UdpPlcClient"/>.
/// </summary>
public sealed class UdpPlcClientBuilder
{
    private string _host = "127.0.0.1";
    private int _port = 2000;
    private TimeSpan _timeout = TimeSpan.FromSeconds(5);
    private ILogger<UdpPlcClient>? _logger;
    private readonly TelegramRegistry _registry = new();
    private ByteOrder _byteOrder = ByteOrder.BigEndian;

    /// <summary>
    /// Sets the remote host and UDP port to send datagrams to.
    /// </summary>
    /// <param name="host">
    /// The hostname or IPv4/IPv6 address of the remote PLC or device.
    /// Example: <c>"192.168.1.100"</c> for a device on the local network.
    /// </param>
    /// <param name="port">
    /// The UDP port number the remote device is listening on.
    /// </param>
    public UdpPlcClientBuilder SendTo(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>
    /// Send and receive timeout for the underlying UDP socket.
    /// If no data is sent or received within this period, the operation fails.
    /// </summary>
    /// <param name="timeout">
    /// The socket-level timeout for both send and receive operations.
    /// Default: <c>5 seconds</c>. UDP is connectionless, so this controls how long
    /// a blocked receive will wait before throwing.
    /// </param>
    public UdpPlcClientBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets the byte order used to serialise and deserialise all multi-byte data fields
    /// (Word, Int, DWord, Real, …) on this connection.
    /// </summary>
    /// <param name="byteOrder">
    /// <list type="bullet">
    ///   <item>
    ///     <term><see cref="ByteOrder.BigEndian"/> (default)</term>
    ///     <description>
    ///       Siemens S7 PLC wire format. Use this for all standard S7-300/400/1200/1500 PLCs.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="ByteOrder.LittleEndian"/></term>
    ///     <description>
    ///       Windows/Linux device wire format. Use this when communicating with PCs,
    ///       embedded Linux devices, or any non-PLC endpoint using native x86/ARM byte order.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// The 2-byte <c>TelegramId</c> header is always transmitted big-endian regardless
    /// of this setting — only data fields are affected.
    /// </para>
    /// </param>
    public UdpPlcClientBuilder WithByteOrder(ByteOrder byteOrder)
    {
        _byteOrder = byteOrder;
        return this;
    }

    /// <summary>
    /// Attaches a Microsoft.Extensions.Logging <see cref="ILogger{TCategoryName}"/> for
    /// structured diagnostic output (send/receive errors, dispatch warnings, etc.).
    /// </summary>
    /// <param name="logger">
    /// The logger instance. Obtain one from your DI container via
    /// <c>loggerFactory.CreateLogger&lt;UdpPlcClient&gt;()</c>.
    /// If omitted, no log output is produced.
    /// </param>
    public UdpPlcClientBuilder WithLogger(ILogger<UdpPlcClient> logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Registers a source-generated typed telegram so that incoming datagrams with a matching
    /// <c>TelegramId</c> are deserialised and dispatched to any <c>Subscribe&lt;T&gt;</c>
    /// handlers. Uses <c>T.Definition</c> — zero reflection.
    /// </summary>
    /// <typeparam name="T">
    /// A <c>partial</c> class decorated with <c>[S7Telegram(messageId: …)]</c> and processed
    /// by the Roslyn source generator.
    /// </typeparam>
    public UdpPlcClientBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
    {
        _registry.Register(T.Definition);
        return this;
    }

    /// <summary>
    /// Registers a hand-crafted <see cref="TelegramDefinition"/> for legacy or dynamic
    /// telegram dispatch without the source generator.
    /// </summary>
    /// <param name="definition">
    /// A manually constructed definition. Definitions with <c>TelegramId == 0</c>
    /// fall back to size-based matching.
    /// </param>
    public UdpPlcClientBuilder RegisterTelegram(TelegramDefinition definition)
    {
        _registry.Register(definition);
        return this;
    }

    /// <summary>
    /// Builds and returns a fully configured <see cref="UdpPlcClient"/>.
    /// Call <see cref="UdpPlcClient.StartAsync"/> on the returned instance to begin
    /// sending and receiving datagrams.
    /// </summary>
    public UdpPlcClient Build()
    {
        var config = new ConnectionConfiguration
        {
            Host = _host,
            Port = _port,
            TimeoutMs = (int)_timeout.TotalMilliseconds,
        };
        return new UdpPlcClient(config, _registry, _logger, _byteOrder);
    }
}
