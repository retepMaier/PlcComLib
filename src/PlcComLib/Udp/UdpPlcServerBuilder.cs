using Microsoft.Extensions.Logging;
using PlcComLib.Core;
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

    /// <summary>Sets the local address and port to listen on.</summary>
    public UdpPlcServerBuilder ListenOn(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>Attach a logger.</summary>
    public UdpPlcServerBuilder WithLogger(ILogger<UdpPlcServer> logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Registers a source-generated typed telegram.
    /// The telegram's <c>Definition</c> is read directly from the static interface member — no reflection.
    /// </summary>
    public UdpPlcServerBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
    {
        _registry.Register(T.Definition);
        return this;
    }

    /// <summary>Registers a hand-crafted <see cref="TelegramDefinition"/> directly.</summary>
    public UdpPlcServerBuilder RegisterTelegram(TelegramDefinition definition)
    {
        _registry.Register(definition);
        return this;
    }

    /// <summary>Builds and returns a configured <see cref="UdpPlcServer"/>.</summary>
    public UdpPlcServer Build()
    {
        var config = new ConnectionConfiguration
        {
            Host = _host,
            Port = _port,
            Mode = ConnectionMode.Server,
        };
        return new UdpPlcServer(config, _registry, _logger);
    }
}
