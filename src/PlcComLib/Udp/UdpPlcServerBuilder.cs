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

    /// <summary>Local IP address and UDP port to listen on. Use <c>"0.0.0.0"</c> for all interfaces.</summary>
    public UdpPlcServerBuilder ListenOn(string host, int port)
    {
        _host = host;
        _port = port;
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
        _registry.Register(T.Definition);
        _lastRegisteredDef = T.Definition;
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
    /// Sets the TelegramId for the most recently registered telegram.
    /// </summary>
    /// <param name="messageId">Unique 2-byte identifier for this telegram type.</param>
    public UdpPlcServerBuilder WithMessageId(ushort messageId)
    {
        if (_lastRegisteredDef is not null)
            _lastRegisteredDef.MessageId = messageId;
        return this;
    }

    /// <summary>
    /// Sets the expected total wire size for the most recently registered telegram.
    /// For source-generated telegrams, pass <c>T.WireSize</c>.
    /// </summary>
    public UdpPlcServerBuilder WithLength(int wireSize)
    {
        if (_lastRegisteredDef is not null)
            _lastRegisteredDef.ConfiguredWireSize = wireSize;
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
        };
        return new UdpPlcServer(config, _registry, _logger, _byteOrder);
    }
}
