using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlcComLib.Core;
using PlcComLib.Core.Events;
using PlcComLib.Core.Internal;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Telegrams;

namespace PlcComLib.Tcp;

/// <summary>
/// TCP server that accepts incoming PLC connections.
/// Manages multiple concurrent clients. Thread-safe.
/// </summary>
public sealed partial class TcpPlcServer(
    ConnectionConfiguration config,
    TelegramRegistry registry,
    ILogger<TcpPlcServer>? logger = null,
    ByteOrder byteOrder = ByteOrder.BigEndian) : IPlcConnection
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;
    private readonly ConnectionConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly TelegramIdFramer _framer = new(registry.Definitions, byteOrder);
    private readonly TelegramDispatcher _dispatcher = new(registry, byteOrder, logger);

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private readonly ConcurrentDictionary<Guid, ClientContext> _clients = new();
    private volatile bool _isConnected;
    private bool _disposed;

    public event EventHandler<TelegramReceivedEventArgs>? TelegramReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// Fired when a received payload cannot be matched to any registered telegram definition.
    /// Subscribe to inspect or log unrecognised messages.
    /// </summary>
    public event EventHandler<UnknownTelegramEventArgs>? UnknownTelegramReceived;

    /// <summary>Fired immediately after raw bytes are written to a client socket.</summary>
    public event EventHandler<RawBytesEventArgs>? RawBytesSent;

    /// <summary>Fired immediately after raw bytes are read from a client socket.</summary>
    public event EventHandler<RawBytesEventArgs>? RawBytesReceived;

    public bool IsConnected => _isConnected;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var endpoint = new IPEndPoint(IPAddress.Parse(_config.Host), _config.Port);
        _listener = new TcpListener(endpoint);
        _listener.Start(_config.MaxConnections);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isConnected = true;
        EventRaiser.Raise(ConnectionStateChanged, this, new ConnectionStateChangedEventArgs(true, "Server started"), _logger);
        LogServerListening(_logger, _config.Host, _config.Port);
        _acceptTask = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null) await _cts.CancelAsync();
        _listener?.Stop();
        if (_acceptTask != null)
            try { await _acceptTask.WaitAsync(cancellationToken); } catch { /* ignore */ }
        foreach (var ctx in _clients.Values) ctx.Dispose();
        _clients.Clear();
        _isConnected = false;
        EventRaiser.Raise(ConnectionStateChanged, this, new ConnectionStateChangedEventArgs(false, "Server stopped"), _logger);
    }

    // ── Send (broadcast) ──────────────────────────────────────────────────────

    /// <summary>Broadcasts a legacy untyped telegram to all connected clients.</summary>
    public async Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var framed = _framer.Frame(TelegramSerializer.Serialize(telegram, byteOrder));
        await Task.WhenAll(_clients.Values.Select(async c =>
        {
            await c.SendAsync(framed, cancellationToken);
            EventRaiser.Raise(RawBytesSent, this, new RawBytesEventArgs(framed, c.RemoteAddress, c.Port), _logger);
        }));
    }

    /// <summary>Broadcasts a strongly-typed telegram to all connected clients.</summary>
    public async Task SendAsync<T>(T telegram, CancellationToken cancellationToken = default)where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var framed = _framer.Frame(_dispatcher.Serialize(telegram));
        await Task.WhenAll(_clients.Values.Select(async c =>
        {
            await c.SendAsync(framed, cancellationToken);
            EventRaiser.Raise(RawBytesSent, this, new RawBytesEventArgs(framed, c.RemoteAddress, c.Port), _logger);
        }));
    }

    // ── Send (unicast) ────────────────────────────────────────────────────────

    /// <summary>Sends a legacy untyped telegram to a specific client.</summary>
    public async Task SendToAsync(Guid clientId, Telegram telegram, CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var ctx))
            throw new KeyNotFoundException($"Client {clientId} not found.");
        var framed = _framer.Frame(TelegramSerializer.Serialize(telegram, byteOrder));
        await ctx.SendAsync(framed, cancellationToken);
        EventRaiser.Raise(RawBytesSent, this, new RawBytesEventArgs(framed, ctx.RemoteAddress, ctx.Port), _logger);
    }

    /// <summary>Sends a legacy untyped telegram to a specific client identified by remote IP address and port.</summary>
    public async Task SendToAsync(string remoteAddress, int port, Telegram telegram, CancellationToken cancellationToken = default)
    {
        var ctx = FindClient(remoteAddress, port);
        var framed = _framer.Frame(TelegramSerializer.Serialize(telegram, byteOrder));
        await ctx.SendAsync(framed, cancellationToken);
        EventRaiser.Raise(RawBytesSent, this, new RawBytesEventArgs(framed, ctx.RemoteAddress, ctx.Port), _logger);
    }

    /// <summary>Sends a strongly-typed telegram to a specific client.</summary>
    public async Task SendToAsync<T>(Guid clientId, T telegram, CancellationToken cancellationToken = default)where T : ITypedS7Telegram<T>
    {
        if (!_clients.TryGetValue(clientId, out var ctx))
            throw new KeyNotFoundException($"Client {clientId} not found.");
        var framed = _framer.Frame(_dispatcher.Serialize(telegram));
        await ctx.SendAsync(framed, cancellationToken);
        EventRaiser.Raise(RawBytesSent, this, new RawBytesEventArgs(framed, ctx.RemoteAddress, ctx.Port), _logger);
    }

    /// <summary>Sends a strongly-typed telegram to a specific client identified by remote IP address and port.</summary>
    public async Task SendToAsync<T>(string remoteAddress, int port, T telegram, CancellationToken cancellationToken = default)where T : ITypedS7Telegram<T>
    {
        var ctx = FindClient(remoteAddress, port);
        var framed = _framer.Frame(_dispatcher.Serialize(telegram));
        await ctx.SendAsync(framed, cancellationToken);
        EventRaiser.Raise(RawBytesSent, this, new RawBytesEventArgs(framed, ctx.RemoteAddress, ctx.Port), _logger);
    }

    private ClientContext FindClient(string remoteAddress, int port)
    {
        var ctx = _clients.Values.FirstOrDefault(c =>
            string.Equals(c.RemoteAddress, remoteAddress, StringComparison.Ordinal) && c.Port == port);
        if (ctx is null)
            throw new KeyNotFoundException($"No connected client found at {remoteAddress}:{port}.");
        return ctx;
    }

    // ── Typed subscription ────────────────────────────────────────────────────

    /// <summary>
    /// Invokes <paramref name="handler"/> whenever a telegram of type <typeparamref name="T"/>
    /// (as registered on this server's builder) arrives from any client. Handlers run independently
    /// of <see cref="TelegramReceived"/>; an exception in one is logged and does not affect the other.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> that unsubscribes when disposed.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> was not registered on the builder.</exception>
    public IDisposable Subscribe<T>(Action<T> handler) where T : ITypedS7Telegram<T>
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _dispatcher.Subscribe<T>((telegram, _, _) => handler(telegram));
    }

    /// <summary>
    /// Invokes <paramref name="handler"/> whenever a telegram of type <typeparamref name="T"/>
    /// (as registered on this server's builder) arrives from any client.
    /// The handler receives the deserialised telegram plus the remote IP address and port.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> that unsubscribes when disposed.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> was not registered on the builder.</exception>
    public IDisposable Subscribe<T>(Action<T, string, int> handler) where T : ITypedS7Telegram<T>
        => _dispatcher.Subscribe(handler);

    // ── Accept / receive loops ────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LogAcceptError(_logger, ex); break; }

            // Apply low-latency socket options to each accepted connection.
            client.NoDelay = _config.NoDelay;
            if (_config.ReceiveBufferSize > 0) client.ReceiveBufferSize = _config.ReceiveBufferSize;
            if (_config.SendBufferSize    > 0) client.SendBufferSize    = _config.SendBufferSize;

            var ctx = new ClientContext(client, Guid.NewGuid());
            _clients[ctx.Id] = ctx;
            LogClientConnected(_logger, ctx.Id, client.Client.RemoteEndPoint);

            // Run the per-client handler without awaiting, but ensure unhandled exceptions are logged.
            _ = HandleClientAsync(ctx, ct).ContinueWith(
                t => LogUnhandledClientException(_logger, t.Exception, ctx.Id),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }

    private async Task HandleClientAsync(ClientContext ctx, CancellationToken ct)
    {
        var buffer      = new byte[65536];
        var accumulator = new FrameAccumulator(_framer, _framer.MaxFrameSize + buffer.Length);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int bytesRead;
                try { bytesRead = await ctx.Stream.ReadAsync(buffer, ct); }
                catch { break; }
                if (bytesRead == 0) break;
                // Only materialise a separate copy for the diagnostic event when someone is listening.
                if (RawBytesReceived is { } rawReceived)
                    EventRaiser.Raise(rawReceived, this, new RawBytesEventArgs(buffer.AsSpan(0, bytesRead).ToArray(), ctx.RemoteAddress, ctx.Port), _logger);

                accumulator.Append(buffer.AsSpan(0, bytesRead));
                int discarded = accumulator.Drain(payload => DispatchTelegram(payload, ctx));
                if (discarded > 0) LogUnrecognisedTelegramId(_logger, discarded);
            }
        }
        finally
        {
            _clients.TryRemove(ctx.Id, out _);
            ctx.Dispose();
            LogClientDisconnected(_logger, ctx.Id);
        }
    }

    private void DispatchTelegram(byte[] payload, ClientContext ctx)
    {
        var def = _dispatcher.Match(payload, ctx.Source);
        if (def is null)
        {
            EventRaiser.Raise(UnknownTelegramReceived, this, new UnknownTelegramEventArgs(payload, byteOrder), _logger);
            return;
        }
        _dispatcher.Deliver(def, payload, ctx.RemoteAddress, ctx.Port, this, TelegramReceived);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        _cts?.Dispose();
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class ClientContext : IDisposable
    {
        private readonly TcpClient _client;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public Guid   Id            { get; }
        public string RemoteAddress { get; }
        public int    Port          { get; }
        public NetworkStream Stream { get; }

        /// <summary>Client description used in log messages.</summary>
        public string Source => $"client {Id} ({RemoteAddress}:{Port})";

        public ClientContext(TcpClient client, Guid id)
        {
            _client = client;
            Id      = id;
            Stream  = client.GetStream();

            if (client.Client.RemoteEndPoint is System.Net.IPEndPoint ep)
            {
                RemoteAddress = ep.Address.ToString();
                Port          = ep.Port;
            }
            else
            {
                RemoteAddress = string.Empty;
                Port          = 0;
            }
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct);
            try { await Stream.WriteAsync(data, ct); await Stream.FlushAsync(ct); }
            finally { _writeLock.Release(); }
        }

        public void Dispose()
        {
            _writeLock.Dispose();
            try { _client.Dispose(); } catch { /* ignore */ }
        }
    }
}