using Microsoft.Extensions.Logging;
using PlcComLib.Core;
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
    private ILogger<TcpPlcClient>? _logger;
    private readonly TelegramRegistry _registry = new();

    /// <summary>Sets the remote PLC host and port.</summary>
    public TcpPlcClientBuilder ConnectTo(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>How long to wait between reconnection attempts. Default: 5 s.</summary>
    public TcpPlcClientBuilder WithReconnectInterval(TimeSpan interval)
    {
        _reconnectInterval = interval;
        return this;
    }

    /// <summary>Send/receive timeout. Default: 10 s.</summary>
    public TcpPlcClientBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>Use the default 4-byte big-endian length-prefix framer.</summary>
    public TcpPlcClientBuilder WithLengthPrefixFramer()
    {
        _framer = new LengthPrefixFramer();
        return this;
    }

    /// <summary>Use a fixed-length framer (no header overhead).</summary>
    public TcpPlcClientBuilder WithFixedLengthFramer(int frameSize)
    {
        _framer = new FixedLengthFramer(frameSize);
        return this;
    }

    /// <summary>Provide a custom framer implementation.</summary>
    public TcpPlcClientBuilder WithFramer(IMessageFramer framer)
    {
        _framer = framer;
        return this;
    }

    /// <summary>Attach a logger.</summary>
    public TcpPlcClientBuilder WithLogger(ILogger<TcpPlcClient> logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Registers a source-generated typed telegram.
    /// The telegram's <c>Definition</c> is read directly from the static interface member — no reflection.
    /// </summary>
    public TcpPlcClientBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
    {
        _registry.Register(T.Definition);
        return this;
    }

    /// <summary>Registers a hand-crafted <see cref="TelegramDefinition"/> directly.</summary>
    public TcpPlcClientBuilder RegisterTelegram(TelegramDefinition definition)
    {
        _registry.Register(definition);
        return this;
    }

    /// <summary>Builds and returns a configured <see cref="TcpPlcClient"/>.</summary>
    public TcpPlcClient Build()
    {
        var config = new ConnectionConfiguration
        {
            Host = _host,
            Port = _port,
            ReconnectIntervalMs = (int)_reconnectInterval.TotalMilliseconds,
            TimeoutMs = (int)_timeout.TotalMilliseconds,
        };
        return new TcpPlcClient(config, _registry, _framer, _logger);
    }
}
