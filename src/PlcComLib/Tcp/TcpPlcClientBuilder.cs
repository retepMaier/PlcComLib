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
    private bool _useLengthFramer;
    private ILogger<TcpPlcClient>? _logger;
    private readonly TelegramRegistry _registry = new();
    private ByteOrder _byteOrder = ByteOrder.BigEndian;
    private TelegramDefinition? _lastRegisteredDef;

    /// <summary>Sets the remote host and TCP port to connect to.</summary>
    public TcpPlcClientBuilder ConnectTo(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>How long to wait between automatic reconnection attempts. Default: <c>5 s</c>.</summary>
    public TcpPlcClientBuilder WithReconnectInterval(TimeSpan interval)
    {
        _reconnectInterval = interval;
        return this;
    }

    /// <summary>Send and receive socket timeout. Default: <c>10 s</c>.</summary>
    public TcpPlcClientBuilder WithTimeout(TimeSpan timeout)
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
    public TcpPlcClientBuilder WithLengthFramer()
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
    public TcpPlcClientBuilder WithFramer(IMessageFramer framer)
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
    public TcpPlcClientBuilder WithByteOrder(ByteOrder byteOrder)
    {
        _byteOrder = byteOrder;
        return this;
    }

    /// <summary>Attaches a <see cref="ILogger{TCategoryName}"/> for structured diagnostic output.</summary>
    public TcpPlcClientBuilder WithLogger(ILogger<TcpPlcClient> logger)
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
    public TcpPlcClientBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
    {
        _registry.Register(T.Definition);
        _lastRegisteredDef = T.Definition;
        return this;
    }

    /// <summary>
    /// Registers a hand-crafted <see cref="TelegramDefinition"/> for legacy or dynamic
    /// telegram dispatch without the source generator. Chain <see cref="WithMessageId"/>
    /// and <see cref="WithLength"/> to set the TelegramId and wire size.
    /// </summary>
    public TcpPlcClientBuilder RegisterTelegram(TelegramDefinition definition)
    {
        _registry.Register(definition);
        _lastRegisteredDef = definition;
        return this;
    }

    /// <summary>
    /// Sets the TelegramId for the most recently registered telegram.
    /// The 2-byte id is written as the first two bytes of every serialised payload
    /// and used to route incoming messages to the correct handler.
    /// </summary>
    /// <param name="messageId">Unique 2-byte identifier for this telegram type.</param>
    public TcpPlcClientBuilder WithMessageId(ushort messageId)
    {
        if (_lastRegisteredDef is not null)
            _lastRegisteredDef.MessageId = messageId;
        return this;
    }

    /// <summary>
    /// Sets the TelegramId for the most recently registered telegram, together with the
    /// byte offset and S7 data type used to read that id from the received payload.
    /// </summary>
    /// <typeparam name="TType">
    /// S7 data type marker (e.g. <see cref="PlcComLib.DataTypes.S7Word"/>,
    /// <see cref="PlcComLib.DataTypes.S7Int"/>) that determines the field width and
    /// interpretation at <paramref name="byteOffset"/>.
    /// </typeparam>
    /// <param name="id">Expected id value for this telegram type.</param>
    /// <param name="byteOffset">Zero-based byte offset in the payload where the id is located.</param>
    public TcpPlcClientBuilder WithMessageId<TType>(ushort id, int byteOffset)
        where TType : IS7FramingType
    {
        if (_lastRegisteredDef is not null)
        {
            _lastRegisteredDef.MessageId           = id;
            _lastRegisteredDef.MessageIdByteOffset = byteOffset;
            _lastRegisteredDef.MessageIdDataType   = TType.DataType;
        }
        return this;
    }

    /// <summary>
    /// Sets the expected total wire size (in bytes) for the most recently registered telegram.
    /// Used by the <see cref="TelegramIdFramer"/> to determine message boundaries.
    /// For source-generated telegrams, pass <c>T.WireSize</c>.
    /// </summary>
    /// <param name="wireSize">Total wire size including the 2-byte TelegramId header.</param>
    public TcpPlcClientBuilder WithLength(int wireSize)
    {
        if (_lastRegisteredDef is not null)
            _lastRegisteredDef.ConfiguredWireSize = wireSize;
        return this;
    }

    /// <summary>
    /// Sets the expected total wire size for the most recently registered telegram and
    /// configures a length-field validation check.
    /// When a frame is received the value at <paramref name="byteOffset"/> (read as
    /// <typeparamref name="TType"/>) is compared to <paramref name="length"/>; a mismatch
    /// triggers a log warning and raises <c>UnknownTelegramReceived</c>.
    /// </summary>
    /// <typeparam name="TType">
    /// S7 data type marker (e.g. <see cref="PlcComLib.DataTypes.S7Word"/>,
    /// <see cref="PlcComLib.DataTypes.S7Int"/>) that determines the field width and
    /// interpretation at <paramref name="byteOffset"/>.
    /// </typeparam>
    /// <param name="length">Expected total length in bytes.</param>
    /// <param name="byteOffset">Zero-based byte offset in the payload where the length field is located.</param>
    public TcpPlcClientBuilder WithLength<TType>(int length, int byteOffset)
        where TType : IS7FramingType
    {
        if (_lastRegisteredDef is not null)
        {
            _lastRegisteredDef.ConfiguredWireSize = length;
            _lastRegisteredDef.LengthByteOffset   = byteOffset;
            _lastRegisteredDef.LengthDataType     = TType.DataType;
        }
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
        // Default framer: TelegramIdFramer — reads the first 2 bytes as TelegramId and
        // uses the registered wire size (from WithLength / field definitions) for framing.
        var framer = _framer ?? (_useLengthFramer
            ? new LengthFramer(_byteOrder)
            : (IMessageFramer)new TelegramIdFramer(_registry.Definitions, _byteOrder));
        return new TcpPlcClient(config, _registry, framer, _logger, _byteOrder);
    }
}
