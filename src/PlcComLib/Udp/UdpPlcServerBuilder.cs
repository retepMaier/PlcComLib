using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.Udp;

/// <summary>
/// Fluent builder for <see cref="UdpPlcServer"/>.
/// </summary>
public sealed class UdpPlcServerBuilder
{
    private string _host = "0.0.0.0";
    private int _port = 2000;
    private ILogger<UdpPlcServer>? _logger;
    private readonly TelegramRegistry _registry = new();
    private ByteOrder _byteOrder = ByteOrder.BigEndian;
    private TelegramDefinition? _lastRegisteredDef;

    /// <summary>The definitions registered so far (exposed for tests).</summary>
    internal TelegramRegistry Registry => _registry;
    private int _receiveBufferSize = 0;
    private int _sendBufferSize = 0;

    /// <summary>Local IP address and UDP port to listen on. Use <c>"0.0.0.0"</c> for all interfaces.</summary>
    public UdpPlcServerBuilder ListenOn(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>
    /// Sets the socket receive buffer size (SO_RCVBUF). <c>0</c> leaves the OS default unchanged.
    /// Increase for high-throughput connections to reduce datagram loss under burst load.
    /// </summary>
    public UdpPlcServerBuilder WithReceiveBufferSize(int size)
    {
        _receiveBufferSize = size;
        return this;
    }

    /// <summary>
    /// Sets the socket send buffer size (SO_SNDBUF). <c>0</c> leaves the OS default unchanged.
    /// Increase for high-throughput connections to improve send performance.
    /// </summary>
    public UdpPlcServerBuilder WithSendBufferSize(int size)
    {
        _sendBufferSize = size;
        return this;
    }

    /// <summary>
    /// Sets the byte order for all multi-byte data fields on this server.
    /// <see cref="ByteOrder.BigEndian"/> (default) for Siemens S7 PLCs;
    /// <see cref="ByteOrder.LittleEndian"/> for Windows/Linux devices.
    /// </summary>
    public UdpPlcServerBuilder WithByteOrder(ByteOrder byteOrder)
    {
        _byteOrder = byteOrder;
        return this;
    }

    /// <summary>Attaches a <see cref="ILogger{TCategoryName}"/> for structured diagnostic output.</summary>
    public UdpPlcServerBuilder WithLogger(ILogger<UdpPlcServer> logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Registers a source-generated typed telegram so that incoming datagrams with a matching
    /// TelegramId are deserialised and dispatched to any <c>Subscribe&lt;T&gt;</c> handlers.
    /// Chain <see cref="WithMessageId"/> and <see cref="WithLength"/> to configure the
    /// TelegramId and wire size used for dispatch:
    /// <code>
    /// .RegisterTelegram&lt;SensorReading&gt;()
    ///     .WithMessageId(0x0010)
    ///     .WithLength(SensorReading.WireSize)
    /// </code>
    /// </summary>
    public UdpPlcServerBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
    {
        // Register a private copy: WithMessageId/WithLength must not change the shared
        // T.Definition, which other connections registering the same type also start from.
        var definition = T.Definition.Clone();
        _registry.Register(definition);
        _lastRegisteredDef = definition;
        return this;
    }

    /// <summary>
    /// Registers a hand-crafted <see cref="TelegramDefinition"/> for legacy or dynamic
    /// telegram dispatch without the source generator.
    /// </summary>
    public UdpPlcServerBuilder RegisterTelegram(TelegramDefinition definition)
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
    /// S7 data type marker (e.g. <see cref="Core.PlcTypes.S7Word"/>,
    /// <see cref="Core.PlcTypes.S7Int"/>, <see cref="Core.PlcTypes.S7DWord"/>)
    /// that determines the field width and interpretation at <paramref name="byteOffset"/>.
    /// </typeparam>
    /// <param name="id">Expected id value for this telegram type. Any numeric value is accepted.</param>
    /// <param name="byteOffset">Zero-based byte offset in the payload where the id is located.</param>
    public UdpPlcServerBuilder WithMessageId<TType>(long id, int byteOffset)where TType : IS7FramingType
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
    public UdpPlcServerBuilder WithMessageId<TTelegram, TField>(
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

        int offset = TelegramBuilderSupport.GetFieldOffset(_lastRegisteredDef, propName, nameof(fieldSelector));

        _lastRegisteredDef.MessageId           = id;
        _lastRegisteredDef.MessageIdByteOffset = offset;
        _lastRegisteredDef.MessageIdDataType   = TField.DataType;
        return this;
    }

    /// <summary>
    /// Sets the expected total wire size for the most recently registered telegram and
    /// configures a length-field validation check.
    /// When a datagram is received the value at <paramref name="byteOffset"/> (read as
    /// <typeparamref name="TType"/>) is compared to <paramref name="length"/>; a mismatch
    /// triggers a log warning and raises <c>UnknownTelegramReceived</c>.
    /// </summary>
    /// <typeparam name="TType">
    /// S7 data type marker (e.g. <see cref="Core.PlcTypes.S7Word"/>,
    /// <see cref="Core.PlcTypes.S7Int"/>) that determines the field width and
    /// interpretation at <paramref name="byteOffset"/>.
    /// </typeparam>
    /// <param name="length">Expected total length in bytes. Any numeric value is accepted.</param>
    /// <param name="byteOffset">Zero-based byte offset in the payload where the length field is located.</param>
    public UdpPlcServerBuilder WithLength<TType>(long length, int byteOffset)
        where TType : IS7FramingType
    {
        if (_lastRegisteredDef is not null)
        {
            _lastRegisteredDef.ConfiguredWireSize = TelegramBuilderSupport.CheckLength(_lastRegisteredDef, length);
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
    public UdpPlcServerBuilder WithLength<TTelegram, TField>(
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

        int offset = TelegramBuilderSupport.GetFieldOffset(_lastRegisteredDef, propName, nameof(fieldSelector));

        _lastRegisteredDef.ConfiguredWireSize = TelegramBuilderSupport.CheckLength(_lastRegisteredDef, length);
        _lastRegisteredDef.LengthByteOffset   = offset;
        _lastRegisteredDef.LengthDataType     = TField.DataType;
        return this;
    }

    /// <summary>
    /// Builds and returns a fully configured <see cref="UdpPlcServer"/>.
    /// Call <see cref="UdpPlcServer.StartAsync"/> to begin listening for datagrams.
    /// </summary>
    public UdpPlcServer Build()
    {
        var config = new ConnectionConfiguration
        {
            Host = _host,
            Port = _port,
            Mode = ConnectionMode.Server,
            ReceiveBufferSize = _receiveBufferSize,
            SendBufferSize = _sendBufferSize,
        };
        return new UdpPlcServer(config, _registry, _logger, _byteOrder);
    }
}
