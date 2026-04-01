using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Telegrams;

namespace PlcComLib.Tcp;

/// <summary>
/// Fluent builder for <see cref="TcpPlcServer"/>.
/// </summary>
public sealed class TcpPlcServerBuilder
{
    private string _host = "0.0.0.0";
    private int _port = 2000;
    private int _maxConnections = 10;
    private TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private IMessageFramer? _framer;
    private ILogger<TcpPlcServer>? _logger;
    private readonly TelegramRegistry _registry = new();
    private ByteOrder _byteOrder = ByteOrder.BigEndian;

    /// <summary>
    /// Sets the local IP address and TCP port the server will bind to and listen on.
    /// </summary>
    /// <param name="host">
    /// The local IP address to bind to. Use <c>"0.0.0.0"</c> (default) to listen on all
    /// network interfaces, or a specific address such as <c>"192.168.1.10"</c> to restrict
    /// incoming connections to a single interface.
    /// </param>
    /// <param name="port">
    /// The TCP port number to listen on. Must match the port configured on the connecting
    /// PLC or client. Siemens S7 custom TCP communication typically uses port <c>2000</c>.
    /// </param>
    public TcpPlcServerBuilder ListenOn(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>
    /// Maximum number of concurrent client connections the server will accept simultaneously.
    /// </summary>
    /// <param name="max">
    /// The connection backlog and concurrency limit. Connections beyond this limit are
    /// refused by the OS until an existing client disconnects. Default: <c>10</c>.
    /// For high-availability scenarios with many PLCs reporting simultaneously, increase
    /// this to match the expected number of concurrent connections.
    /// </param>
    public TcpPlcServerBuilder WithMaxConnections(int max)
    {
        _maxConnections = max;
        return this;
    }

    /// <summary>
    /// Send and receive timeout for each accepted client socket.
    /// If a client does not send or receive data within this period, the operation
    /// fails and the client connection is closed.
    /// </summary>
    /// <param name="timeout">
    /// The per-client socket timeout. Default: <c>10 seconds</c>. Set to a value
    /// longer than the PLC's data-send cycle to avoid dropping slow clients.
    /// </param>
    public TcpPlcServerBuilder WithTimeout(TimeSpan timeout)
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
    public TcpPlcServerBuilder WithLengthPrefixFramer()
    {
        _framer = new LengthPrefixFramer();
        return this;
    }

    /// <summary>
    /// Use the built-in 2-byte payload-embedded length framer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire format: <c>[TotalLength: UInt16 BE (2 bytes)][Payload: byte * (TotalLength - 2)]</c>.
    /// The first 2 bytes of every message encode the total frame length including those 2 bytes.
    /// </para>
    /// <para>
    /// Use this framer when communicating with a Siemens PLC via TSEND/TRCV, where the length
    /// field is a normal WORD variable at the start of the DB block and is part of the raw payload.
    /// </para>
    /// </remarks>
    public TcpPlcServerBuilder WithPayloadLengthFramer()
    {
        _framer = new PayloadLengthFramer();
        return this;
    }

    /// <summary>
    /// Plug in a custom <see cref="IMessageFramer"/> implementation.
    /// Use this when connecting PLCs or devices that use a proprietary framing protocol
    /// (e.g. STX/ETX delimiters, SLIP encoding, or a custom header structure).
    /// </summary>
    /// <param name="framer">
    /// Your custom framer. Must implement both <c>Frame(ReadOnlySpan&lt;byte&gt;)</c>
    /// (wraps a payload for sending) and <c>TryExtract</c> (extracts the next complete
    /// message from the receive buffer).
    /// </param>
    public TcpPlcServerBuilder WithFramer(IMessageFramer framer)
    {
        _framer = framer;
        return this;
    }

    /// <summary>
    /// Sets the byte order used to serialise and deserialise all multi-byte data fields
    /// (Word, Int, DWord, Real, …) on this server connection.
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
    ///       Windows/Linux device wire format. Use this when accepting connections from
    ///       PCs, embedded Linux devices, or any non-PLC client that uses native x86/ARM byte order.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// The 2-byte <c>TelegramId</c> header follows the same byte order as data fields.
    /// </para>
    /// </param>
    public TcpPlcServerBuilder WithByteOrder(ByteOrder byteOrder)
    {
        _byteOrder = byteOrder;
        return this;
    }

    /// <summary>
    /// Attaches a Microsoft.Extensions.Logging <see cref="ILogger{TCategoryName}"/> for
    /// structured diagnostic output (client connections, disconnections, dispatch warnings, etc.).
    /// </summary>
    /// <param name="logger">
    /// The logger instance. Obtain one from your DI container via
    /// <c>loggerFactory.CreateLogger&lt;TcpPlcServer&gt;()</c>.
    /// If omitted, no log output is produced.
    /// </param>
    public TcpPlcServerBuilder WithLogger(ILogger<TcpPlcServer> logger)
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
    /// by the Roslyn source generator.
    /// </typeparam>
    public TcpPlcServerBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
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
    public TcpPlcServerBuilder RegisterTelegram(TelegramDefinition definition)
    {
        _registry.Register(definition);
        return this;
    }

    /// <summary>
    /// Builds and returns a fully configured <see cref="TcpPlcServer"/>.
    /// Call <see cref="TcpPlcServer.StartAsync"/> on the returned instance to begin
    /// accepting client connections.
    /// </summary>
    public TcpPlcServer Build()
    {
        var config = new ConnectionConfiguration
        {
            Host = _host,
            Port = _port,
            TimeoutMs = (int)_timeout.TotalMilliseconds,
            MaxConnections = _maxConnections,
            Mode = ConnectionMode.Server,
        };
        return new TcpPlcServer(config, _registry, _framer, _logger, _byteOrder);
    }
}
