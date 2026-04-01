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
    private bool _useLengthFramer;
    private ILogger<TcpPlcServer>? _logger;
    private readonly TelegramRegistry _registry = new();
    private ByteOrder _byteOrder = ByteOrder.BigEndian;
    private TelegramDefinition? _lastRegisteredDef;

    /// <summary>Local address and TCP port to bind to. Use <c>"0.0.0.0"</c> for all interfaces.</summary>
    public TcpPlcServerBuilder ListenOn(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>Maximum concurrent client connections. Default: <c>10</c>.</summary>
    public TcpPlcServerBuilder WithMaxConnections(int max)
    {
        _maxConnections = max;
        return this;
    }

    /// <summary>Per-client send/receive socket timeout. Default: <c>10 s</c>.</summary>
    public TcpPlcServerBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Use the built-in length framer that reads the total frame length from bytes 2–3
    /// of the telegram payload (immediately after the 2-byte TelegramId).
    /// </summary>
    /// <remarks>
    /// Wire format: <c>[TelegramId: UInt16][TotalLength: UInt16][Data fields…]</c>.
    /// Use when the remote device embeds the total message length in the payload.
    /// For most new integrations, prefer the default <see cref="TelegramIdFramer"/>
    /// (configured automatically via <see cref="RegisterTelegram{T}"/>) instead.
    /// </remarks>
    public TcpPlcServerBuilder WithLengthFramer()
    {
        _framer          = null;
        _useLengthFramer = true;
        return this;
    }

    /// <summary>
    /// Plug in a custom <see cref="IMessageFramer"/> implementation.
    /// Use when the remote device uses a proprietary framing protocol
    /// (e.g. STX/ETX delimiters, SLIP encoding, or a custom header structure).
    /// </summary>
    public TcpPlcServerBuilder WithFramer(IMessageFramer framer)
    {
        _framer          = framer;
        _useLengthFramer = false;
        return this;
    }

    /// <summary>
    /// Sets the byte order for all multi-byte data fields on this connection.
    /// <see cref="ByteOrder.BigEndian"/> (default) for Siemens S7 PLCs;
    /// <see cref="ByteOrder.LittleEndian"/> for Windows/Linux devices.
    /// </summary>
    public TcpPlcServerBuilder WithByteOrder(ByteOrder byteOrder)
    {
        _byteOrder = byteOrder;
        return this;
    }

    /// <summary>Attaches a <see cref="ILogger{TCategoryName}"/> for structured diagnostic output.</summary>
    public TcpPlcServerBuilder WithLogger(ILogger<TcpPlcServer> logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Registers a source-generated typed telegram so that incoming payloads with a matching
    /// TelegramId are deserialised and dispatched to any <c>Subscribe&lt;T&gt;</c> handlers.
    /// Chain <see cref="WithMessageId"/> and <see cref="WithLength"/> to configure the
    /// TelegramId and wire size used for dispatch and framing:
    /// <code>
    /// .RegisterTelegram&lt;MachineStatus&gt;()
    ///     .WithMessageId(0x0001)
    ///     .WithLength(MachineStatus.WireSize)
    /// </code>
    /// </summary>
    public TcpPlcServerBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
    {
        _registry.Register(T.Definition);
        _lastRegisteredDef = T.Definition;
        return this;
    }

    /// <summary>
    /// Registers a hand-crafted <see cref="TelegramDefinition"/> for legacy or dynamic
    /// telegram dispatch without the source generator.
    /// </summary>
    public TcpPlcServerBuilder RegisterTelegram(TelegramDefinition definition)
    {
        _registry.Register(definition);
        _lastRegisteredDef = definition;
        return this;
    }

    /// <summary>
    /// Sets the TelegramId for the most recently registered telegram.
    /// </summary>
    /// <param name="messageId">Unique 2-byte identifier for this telegram type.</param>
    public TcpPlcServerBuilder WithMessageId(ushort messageId)
    {
        if (_lastRegisteredDef is not null)
            _lastRegisteredDef.MessageId = messageId;
        return this;
    }

    /// <summary>
    /// Sets the expected total wire size for the most recently registered telegram.
    /// Used by the <see cref="TelegramIdFramer"/> to determine message boundaries.
    /// For source-generated telegrams, pass <c>T.WireSize</c>.
    /// </summary>
    public TcpPlcServerBuilder WithLength(int wireSize)
    {
        if (_lastRegisteredDef is not null)
            _lastRegisteredDef.ConfiguredWireSize = wireSize;
        return this;
    }

    /// <summary>
    /// Builds and returns a fully configured <see cref="TcpPlcServer"/>.
    /// Call <see cref="TcpPlcServer.StartAsync"/> on the returned instance to begin accepting.
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
        var framer = _framer ?? (_useLengthFramer
            ? new LengthFramer(_byteOrder)
            : (IMessageFramer)new TelegramIdFramer(_registry.Definitions, _byteOrder));
        return new TcpPlcServer(config, _registry, framer, _logger, _byteOrder);
    }
}
