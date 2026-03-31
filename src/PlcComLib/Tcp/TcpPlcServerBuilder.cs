using Microsoft.Extensions.Logging;
using PlcComLib.Core;
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
    private ILogger<TcpPlcServer>? _logger;
    private readonly TelegramRegistry _registry = new();

    /// <summary>Sets the local address and port to listen on.</summary>
    public TcpPlcServerBuilder ListenOn(string host, int port)
    {
        _host = host;
        _port = port;
        return this;
    }

    /// <summary>Maximum number of concurrent client connections. Default: 10.</summary>
    public TcpPlcServerBuilder WithMaxConnections(int max)
    {
        _maxConnections = max;
        return this;
    }

    /// <summary>Send/receive timeout. Default: 10 s.</summary>
    public TcpPlcServerBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>Use the default 4-byte big-endian length-prefix framer.</summary>
    public TcpPlcServerBuilder WithLengthPrefixFramer()
    {
        _framer = new LengthPrefixFramer();
        return this;
    }

    /// <summary>Use a fixed-length framer (no header overhead).</summary>
    public TcpPlcServerBuilder WithFixedLengthFramer(int frameSize)
    {
        _framer = new FixedLengthFramer(frameSize);
        return this;
    }

    /// <summary>Provide a custom framer implementation.</summary>
    public TcpPlcServerBuilder WithFramer(IMessageFramer framer)
    {
        _framer = framer;
        return this;
    }

    /// <summary>Attach a logger.</summary>
    public TcpPlcServerBuilder WithLogger(ILogger<TcpPlcServer> logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Registers a source-generated typed telegram.
    /// The telegram's <c>Definition</c> is read directly from the static interface member — no reflection.
    /// </summary>
    public TcpPlcServerBuilder RegisterTelegram<T>() where T : ITypedS7Telegram<T>
    {
        _registry.Register(T.Definition);
        return this;
    }

    /// <summary>Registers a hand-crafted <see cref="TelegramDefinition"/> directly.</summary>
    public TcpPlcServerBuilder RegisterTelegram(TelegramDefinition definition)
    {
        _registry.Register(definition);
        return this;
    }

    /// <summary>Builds and returns a configured <see cref="TcpPlcServer"/>.</summary>
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
        return new TcpPlcServer(config, _registry, _framer, _logger);
    }
}
