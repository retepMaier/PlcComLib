using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Telegrams;

namespace PlcComLib.Tcp;

/// <summary>
/// Fluent builder for <see cref="TcpPlcClient"/>.
/// </summary>
public sealed class TcpPlcClientBuilder
{
    private string _host = "127.0.0.1";
    private int _port = 2000;
    private TimeSpan _reconnectInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private IMessageFramer? _framer;
    private ILogger<TcpPlcClient>? _logger;
    private readonly TelegramRegistry _registry = new();
    private ByteOrder _byteOrder = ByteOrder.BigEndian;

    /// <summary>
    /// Sets the remote host and TCP port to connect to.
    /// </summary>
    /// <param name="host">
    /// The hostname or IPv4/IPv6 address of the PLC or remote device.
    /// Example: <c>"192.168.1.100"</c> for a Siemens S7 PLC on the local network.
    /// </param>
    /// <param name="port">
    /// The TCP port number the remote device is listening on.
    /// Siemens S7 PLCs using custom TCP communication typically use port <c>2000</c>.
    /// </param>
    public TcpPlcClientBuilder ConnectTo(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>
    /// How long to wait between automatic reconnection attempts when the connection
    /// is lost or cannot be established.
    /// </summary>
    /// <param name="interval">
    /// The delay between consecutive reconnection attempts. Shorter intervals mean
    /// faster recovery but higher network load. Longer intervals reduce noise.
    /// Default: <c>5 seconds</c>. Recommended range: 1–30 seconds.
    /// </param>
    public TcpPlcClientBuilder WithReconnectInterval(TimeSpan interval)
    {
        _reconnectInterval = interval;
        return this;
    }

    /// <summary>
    /// Send and receive timeout for the underlying TCP socket.
    /// If no data is sent or received within this period, the operation fails and
    /// the client initiates a reconnection.
    /// </summary>
    /// <param name="timeout">
    /// The socket-level timeout applied to both send and receive operations.
    /// Default: <c>10 seconds</c>. Set to a value longer than the PLC's scan cycle
    /// to avoid spurious disconnections under heavy load.
    /// </param>
    public TcpPlcClientBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Use the built-in 4-byte big-endian length-prefix framer (default).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire format: <c>[Length: UInt32 BE][Payload: byte * Length]</c>.
    /// The 4-byte header encodes the byte-length of the payload that follows.
    /// This is the recommended framer for TCP streams because it handles partial
    /// reads and arbitrary payload sizes without ambiguity.
    /// </para>
    /// <para>
    /// Both sides of the connection (client and server) must use the same framer.
    /// </para>
    /// </remarks>
    public TcpPlcClientBuilder WithLengthPrefixFramer()
    {
        _framer = new LengthPrefixFramer();
        return this;
    }

    /// <summary>
    /// Use a fixed-length framer: every frame is exactly <paramref name="frameSize"/> bytes,
    /// with no framing header.
    /// </summary>
    /// <param name="frameSize">
    /// The exact byte length of every expected message. Must match <c>T.WireSize</c> for
    /// the telegram type you are exchanging. Use this when the remote device sends fixed-size
    /// TCP packets without a length header — for example, some legacy PLC communication
    /// layers that transmit a fixed-size status block at a regular interval.
    /// </param>
    /// <remarks>
    /// Because there is no length header, the receiver buffers incoming bytes until exactly
    /// <paramref name="frameSize"/> bytes are available, then hands them off as one complete
    /// message. Mismatches in frame size cause deserialization errors.
    /// </remarks>
    public TcpPlcClientBuilder WithFixedLengthFramer(int frameSize)
    {
        _framer = new FixedLengthFramer(frameSize);
        return this;
    }

    /// <summary>
    /// Plug in a custom <see cref="IMessageFramer"/> implementation.
    /// Use this when the remote device uses a proprietary framing protocol
    /// (e.g. STX/ETX delimiters, SLIP encoding, or a custom header structure).
    /// </summary>
    /// <param name="framer">
    /// Your custom framer. Must implement both <c>Frame(ReadOnlySpan&lt;byte&gt;)</c>
    /// (wraps a payload for sending) and <c>TryExtract</c> (extracts the next complete
    /// message from the receive buffer).
    /// </param>
    public TcpPlcClientBuilder WithFramer(IMessageFramer framer)
    {
        _framer = framer;
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
    ///       Siemens S7 PLC wire format. All multi-byte values are transmitted
    ///       most-significant-byte first. Use this for all standard S7-300/400/1200/1500 PLCs.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="ByteOrder.LittleEndian"/></term>
    ///     <description>
    ///       Windows/Linux device wire format. All multi-byte values are transmitted
    ///       least-significant-byte first. Use this when connecting to PCs, embedded Linux
    ///       devices, or any non-PLC endpoint that uses native x86/ARM byte order.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// The 2-byte <c>TelegramId</c> header is always transmitted big-endian
    /// regardless of this setting — only data fields are affected.
    /// </para>
    /// </param>
    public TcpPlcClientBuilder WithByteOrder(ByteOrder byteOrder)
    {
        _byteOrder = byteOrder;
        return this;
    }

    /// <summary>
    /// Attaches a Microsoft.Extensions.Logging <see cref="ILogger{TCategoryName}"/> for
    /// structured diagnostic output (connections, reconnects, dispatch warnings, etc.).
    /// </summary>
    /// <param name="logger">
    /// The logger instance. Obtain one from your DI container via
    /// <c>loggerFactory.CreateLogger&lt;TcpPlcClient&gt;()</c>.
    /// If omitted, no log output is produced.
    /// </param>
    public TcpPlcClientBuilder WithLogger(ILogger<TcpPlcClient> logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Registers a source-generated typed telegram so that incoming payloads with a matching
    /// <c>TelegramId</c> are deserialised into <typeparamref name="T"/> and dispatched to
    /// any <c>Subscribe&lt;T&gt;</c> handlers. Uses <c>T.Definition</c> — zero reflection.
    /// </summary>
    /// <typeparam name="T">
    /// A <c>partial</c> class decorated with <c>[S7Telegram(messageId: …)]</c> and processed
    /// by the Roslyn source generator. The generator emits <c>MessageId</c>, <c>WireSize</c>,
    /// <c>Definition</c>, <c>Serialize</c>, and <c>Deserialize</c> at compile time.
    /// </typeparam>
    public TcpPlcClientBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
    {
        _registry.Register(T.Definition);
        return this;
    }

    /// <summary>
    /// Registers a hand-crafted <see cref="TelegramDefinition"/> for legacy or dynamic
    /// telegram dispatch without the source generator.
    /// </summary>
    /// <param name="definition">
    /// A manually constructed definition describing the telegram's fields and
    /// (optionally) its <see cref="TelegramDefinition.TelegramId"/>. Definitions with
    /// <c>TelegramId == 0</c> fall back to size-based matching.
    /// </param>
    public TcpPlcClientBuilder RegisterTelegram(TelegramDefinition definition)
    {
        _registry.Register(definition);
        return this;
    }

    /// <summary>
    /// Builds and returns a fully configured <see cref="TcpPlcClient"/>.
    /// Call <see cref="TcpPlcClient.StartAsync"/> on the returned instance to open the connection.
    /// </summary>
    public TcpPlcClient Build()
    {
        var config = new ConnectionConfiguration
        {
            Host = _host,
            Port = _port,
            ReconnectIntervalMs = (int)_reconnectInterval.TotalMilliseconds,
            TimeoutMs = (int)_timeout.TotalMilliseconds,
        };
        return new TcpPlcClient(config, _registry, _framer, _logger, _byteOrder);
    }
}
