using Microsoft.Extensions.Logging;
using PlcComLib.Core;
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

    /// <summary>Sets the remote host and port to send datagrams to.</summary>
    public UdpPlcClientBuilder SendTo(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>Send/receive timeout. Default: 5 s.</summary>
    public UdpPlcClientBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>Attach a logger.</summary>
    public UdpPlcClientBuilder WithLogger(ILogger<UdpPlcClient> logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Registers a source-generated typed telegram.
    /// The telegram's <c>Definition</c> is read directly from the static interface member — no reflection.
    /// </summary>
    public UdpPlcClientBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
    {
        _registry.Register(T.Definition);
        return this;
    }

    /// <summary>Registers a hand-crafted <see cref="TelegramDefinition"/> directly.</summary>
    public UdpPlcClientBuilder RegisterTelegram(TelegramDefinition definition)
    {
        _registry.Register(definition);
        return this;
    }

    /// <summary>Builds and returns a configured <see cref="UdpPlcClient"/>.</summary>
    public UdpPlcClient Build()
    {
        var config = new ConnectionConfiguration
        {
            Host = _host,
            Port = _port,
            TimeoutMs = (int)_timeout.TotalMilliseconds,
        };
        return new UdpPlcClient(config, _registry, _logger);
    }
}
