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

    /// <summary>
    /// Sets the local IP address and UDP port the server will bind to and listen on.
    /// </summary>
    /// <param name="host">
    /// The local IP address to bind to. Use <c>"0.0.0.0"</c> (default) to listen on all
    /// network interfaces, or a specific address such as <c>"192.168.1.10"</c> to restrict
    /// which interface receives datagrams.
    /// </param>
    /// <param name="port">
    /// The UDP port number to listen on. Must match the destination port used by the
    /// sending PLC or client.
    /// </param>
    public UdpPlcServerBuilder ListenOn(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>
    /// Sets the byte order used to serialise and deserialise all multi-byte data fields
    /// (Word, Int, DWord, Real, …) on this server.
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
    ///       Windows/Linux device wire format. Use this when receiving datagrams from
    ///       PCs, embedded Linux devices, or any non-PLC sender using native x86/ARM byte order.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// The 2-byte <c>TelegramId</c> header follows the same byte order as data fields.
    /// </para>
    /// </param>
    public UdpPlcServerBuilder WithByteOrder(ByteOrder byteOrder)
    {
        _byteOrder = byteOrder;
        return this;
    }

    /// <summary>
    /// Attaches a Microsoft.Extensions.Logging <see cref="ILogger{TCategoryName}"/> for
    /// structured diagnostic output (receive errors, dispatch warnings, etc.).
    /// </summary>
    /// <param name="logger">
    /// The logger instance. Obtain one from your DI container via
    /// <c>loggerFactory.CreateLogger&lt;UdpPlcServer&gt;()</c>.
    /// If omitted, no log output is produced.
    /// </param>
    public UdpPlcServerBuilder WithLogger(ILogger<UdpPlcServer> logger)
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
    public UdpPlcServerBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
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
    public UdpPlcServerBuilder RegisterTelegram(TelegramDefinition definition)
    {
        _registry.Register(definition);
        return this;
    }

    /// <summary>
    /// Builds and returns a fully configured <see cref="UdpPlcServer"/>.
    /// Call <see cref="UdpPlcServer.StartAsync"/> on the returned instance to begin
    /// listening for datagrams.
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
