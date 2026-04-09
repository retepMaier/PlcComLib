using System.Linq.Expressions;
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
    private bool _noDelay = false;
    private int _receiveBufferSize = 0;
    private int _sendBufferSize = 0;

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
    /// Disables Nagle's algorithm (TCP_NODELAY) on each accepted client socket.
    /// Strongly recommended for low-latency PLC communication to avoid up to 200 ms coalescing delays.
    /// Default: <c>false</c>.
    /// </summary>
    public TcpPlcServerBuilder WithNoDelay(bool noDelay = true)
    {
        _noDelay = noDelay;
        return this;
    }

    /// <summary>
    /// Sets the socket receive buffer size (SO_RCVBUF) for each accepted client. <c>0</c> leaves the OS default unchanged.
    /// Increase for high-throughput connections to reduce packet loss under burst load.
    /// </summary>
    public TcpPlcServerBuilder WithReceiveBufferSize(int size)
    {
        _receiveBufferSize = size;
        return this;
    }

    /// <summary>
    /// Sets the socket send buffer size (SO_SNDBUF) for each accepted client. <c>0</c> leaves the OS default unchanged.
    /// Increase for high-throughput connections to improve send performance.
    /// </summary>
    public TcpPlcServerBuilder WithSendBufferSize(int size)
    {
        _sendBufferSize = size;
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
    /// Sets the TelegramId for the most recently registered telegram, together with the
    /// byte offset and S7 data type used to read that id from the received payload.
    /// </summary>
    /// <typeparam name="TType">
    /// S7 data type marker (e.g. <see cref="PlcComLib.DataTypes.S7Word"/>,
    /// <see cref="PlcComLib.DataTypes.S7Int"/>, <see cref="PlcComLib.DataTypes.S7DWord"/>)
    /// that determines the field width and interpretation at <paramref name="byteOffset"/>.
    /// </typeparam>
    /// <param name="id">Expected id value for this telegram type. Any numeric value is accepted.</param>
    /// <param name="byteOffset">Zero-based byte offset in the payload where the id is located.</param>
    public TcpPlcServerBuilder WithMessageId<TType>(long id, int byteOffset)
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
    /// Sets the TelegramId for the most recently registered telegram by referencing
    /// a property of the telegram type <typeparamref name="TTelegram"/> directly.
    /// The byte offset and S7 wire type are derived automatically from the property's
    /// position in the telegram definition — no manual offset calculation needed.
    /// </summary>
    /// <typeparam name="TTelegram">The typed telegram class that owns the field.</typeparam>
    /// <typeparam name="TField">S7 data type of the field (inferred from the lambda).</typeparam>
    /// <param name="id">Expected id value for this telegram type.</param>
    /// <param name="fieldSelector">Lambda pointing to the id field, e.g. <c>(MachineStatus t) => t.TlgId</c>.</param>
    public TcpPlcServerBuilder WithMessageId<TTelegram, TField>(
        long id,
        Expression<Func<TTelegram, TField>> fieldSelector)
        where TTelegram : ITypedS7Telegram<TTelegram>
        where TField    : IS7FramingType
    {
        if (_lastRegisteredDef is null) return this;

        if (fieldSelector.Body is not MemberExpression member)
            throw new ArgumentException(
                "Selector must be a simple property access, e.g. (MachineStatus t) => t.TlgId",
                nameof(fieldSelector));

        string propName = member.Member.Name;

        int offset = 0;
        bool found = false;
        foreach (var field in _lastRegisteredDef.Fields)
        {
            if (field.Name == propName) { found = true; break; }
            offset += field.WireSize;
        }

        if (!found)
            throw new ArgumentException(
                $"Property '{propName}' was not found in the definition for '{_lastRegisteredDef.Id}'.",
                nameof(fieldSelector));

        _lastRegisteredDef.MessageId           = id;
        _lastRegisteredDef.MessageIdByteOffset = offset;
        _lastRegisteredDef.MessageIdDataType   = TField.DataType;
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
    /// <param name="length">Expected total length in bytes. Any numeric value is accepted.</param>
    /// <param name="byteOffset">Zero-based byte offset in the payload where the length field is located.</param>
    public TcpPlcServerBuilder WithLength<TType>(long length, int byteOffset)where TType : IS7FramingType
    {
        if (_lastRegisteredDef is not null)
        {
            _lastRegisteredDef.ConfiguredWireSize = (int)length;
            _lastRegisteredDef.LengthByteOffset   = byteOffset;
            _lastRegisteredDef.LengthDataType     = TType.DataType;
        }
        return this;
    }

    /// <summary>
    /// Sets the expected total wire size for the most recently registered telegram and
    /// configures a length-field validation check by referencing a property of the telegram
    /// type <typeparamref name="TTelegram"/> directly.
    /// The byte offset and S7 wire type are derived automatically from the property's
    /// position in the telegram definition — no manual offset calculation needed.
    /// </summary>
    /// <typeparam name="TTelegram">The typed telegram class that owns the field.</typeparam>
    /// <typeparam name="TField">S7 data type of the field (inferred from the lambda).</typeparam>
    /// <param name="length">Expected total length in bytes.</param>
    /// <param name="fieldSelector">Lambda pointing to the length field, e.g. <c>(MachineStatus t) => t.TlgLength</c>.</param>
    public TcpPlcServerBuilder WithLength<TTelegram, TField>(
        long length,
        Expression<Func<TTelegram, TField>> fieldSelector)
        where TTelegram : ITypedS7Telegram<TTelegram>
        where TField    : IS7FramingType
    {
        if (_lastRegisteredDef is null) return this;

        if (fieldSelector.Body is not MemberExpression member)
            throw new ArgumentException(
                "Selector must be a simple property access, e.g. (MachineStatus t) => t.TlgLength",
                nameof(fieldSelector));

        string propName = member.Member.Name;

        int offset = 0;
        bool found = false;
        foreach (var field in _lastRegisteredDef.Fields)
        {
            if (field.Name == propName) { found = true; break; }
            offset += field.WireSize;
        }

        if (!found)
            throw new ArgumentException(
                $"Property '{propName}' was not found in the definition for '{_lastRegisteredDef.Id}'.",
                nameof(fieldSelector));

        _lastRegisteredDef.ConfiguredWireSize = (int)length;
        _lastRegisteredDef.LengthByteOffset   = offset;
        _lastRegisteredDef.LengthDataType     = TField.DataType;
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
            NoDelay = _noDelay,
            ReceiveBufferSize = _receiveBufferSize,
            SendBufferSize = _sendBufferSize,
        };
        //var framer = _framer ?? (_useLengthFramer
        //    ? new LengthFramer(_byteOrder)
        //    : (IMessageFramer)new TelegramIdFramer(_registry.Definitions, _byteOrder));
        return new TcpPlcServer(config, _registry, _logger, _byteOrder);
    }
}
