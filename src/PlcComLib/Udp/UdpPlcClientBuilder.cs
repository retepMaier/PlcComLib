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
    private TelegramDefinition? _lastRegisteredDef;
    private int _receiveBufferSize = 0;
    private int _sendBufferSize = 0;

    /// <summary>Sets the remote host and UDP port to send datagrams to.</summary>
    public UdpPlcClientBuilder SendTo(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>Socket-level send/receive timeout. Default: <c>5 s</c>.</summary>
    public UdpPlcClientBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets the socket receive buffer size (SO_RCVBUF). <c>0</c> leaves the OS default unchanged.
    /// Increase for high-throughput connections to reduce datagram loss under burst load.
    /// </summary>
    public UdpPlcClientBuilder WithReceiveBufferSize(int size)
    {
        _receiveBufferSize = size;
        return this;
    }

    /// <summary>
    /// Sets the socket send buffer size (SO_SNDBUF). <c>0</c> leaves the OS default unchanged.
    /// Increase for high-throughput connections to improve send performance.
    /// </summary>
    public UdpPlcClientBuilder WithSendBufferSize(int size)
    {
        _sendBufferSize = size;
        return this;
    }

    /// <summary>
    /// Sets the byte order for all multi-byte data fields on this connection.
    /// <see cref="ByteOrder.BigEndian"/> (default) for Siemens S7 PLCs;
    /// <see cref="ByteOrder.LittleEndian"/> for Windows/Linux devices.
    /// </summary>
    public UdpPlcClientBuilder WithByteOrder(ByteOrder byteOrder)
    {
        _byteOrder = byteOrder;
        return this;
    }

    /// <summary>Attaches a <see cref="ILogger{TCategoryName}"/> for structured diagnostic output.</summary>
    public UdpPlcClientBuilder WithLogger(ILogger<UdpPlcClient> logger)
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
    public UdpPlcClientBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
    {
        _registry.Register(T.Definition);
        _lastRegisteredDef = T.Definition;
        return this;
    }

    /// <summary>
    /// Registers a hand-crafted <see cref="TelegramDefinition"/> for legacy or dynamic
    /// telegram dispatch without the source generator.
    /// </summary>
    public UdpPlcClientBuilder RegisterTelegram(TelegramDefinition definition)
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
    public UdpPlcClientBuilder WithMessageId<TType>(long id, int byteOffset) where TType : IS7FramingType
    {
        if (_lastRegisteredDef is not null)
        {
            _lastRegisteredDef.MessageId = id;
            _lastRegisteredDef.MessageIdByteOffset = byteOffset;
            _lastRegisteredDef.MessageIdDataType = TType.DataType;
        }
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
    /// S7 data type marker (e.g. <see cref="PlcComLib.DataTypes.S7Word"/>,
    /// <see cref="PlcComLib.DataTypes.S7Int"/>) that determines the field width and
    /// interpretation at <paramref name="byteOffset"/>.
    /// </typeparam>
    /// <param name="length">Expected total length in bytes. Any numeric value is accepted.</param>
    /// <param name="byteOffset">Zero-based byte offset in the payload where the length field is located.</param>
    public UdpPlcClientBuilder WithLength<TType>(long length, int byteOffset) where TType : IS7FramingType
    {
        if (_lastRegisteredDef is not null)
        {
            _lastRegisteredDef.ConfiguredWireSize = (int)length;
            _lastRegisteredDef.LengthByteOffset = byteOffset;
            _lastRegisteredDef.LengthDataType = TType.DataType;
        }
        return this;
    }

    /// <summary>
    /// Builds and returns a fully configured <see cref="UdpPlcClient"/>.
    /// Call <see cref="UdpPlcClient.StartAsync"/> to begin sending and receiving datagrams.
    /// </summary>
    public UdpPlcClient Build()
    {
        var config = new ConnectionConfiguration
        {
            Host = _host,
            Port = _port,
            TimeoutMs = (int)_timeout.TotalMilliseconds,
            ReceiveBufferSize = _receiveBufferSize,
            SendBufferSize = _sendBufferSize,
        };
        return new UdpPlcClient(config, _registry, _logger, _byteOrder);
    }
}
