using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.Framing;
using PlcComLib.Telegrams;

namespace PlcComLib.Tcp;

/// <summary>
/// TCP server that accepts incoming PLC connections.
/// Manages multiple concurrent clients. Thread-safe.
/// </summary>
public sealed class TcpPlcServer : IPlcConnection
{
    private readonly ConnectionConfiguration _config;
    private readonly TelegramRegistry _registry;
    private readonly IMessageFramer _framer;
    private readonly ILogger<TcpPlcServer>? _logger;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private readonly ConcurrentDictionary<Guid, ClientContext> _clients = new();
    private volatile bool _isConnected;
    private bool _disposed;

    public event EventHandler<TelegramReceivedEventArgs>? TelegramReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public bool IsConnected => _isConnected;

    public TcpPlcServer(
        ConnectionConfiguration config,
        TelegramRegistry registry,
        IMessageFramer? framer = null,
        ILogger<TcpPlcServer>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _framer = framer ?? new LengthPrefixFramer();
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var endpoint = new IPEndPoint(IPAddress.Parse(_config.Host), _config.Port);
        _listener = new TcpListener(endpoint);
        _listener.Start(_config.MaxConnections);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isConnected = true;
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(true, "Server started"));
        _logger?.LogInformation("TCP server listening on {Host}:{Port}", _config.Host, _config.Port);
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
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(false, "Server stopped"));
    }

    public async Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var payload = TelegramSerializer.Serialize(telegram);
        var framed = _framer.Frame(payload);
        var tasks = _clients.Values.Select(c => c.SendAsync(framed, cancellationToken));
        await Task.WhenAll(tasks);
    }

    public async Task SendToAsync(Guid clientId, Telegram telegram, CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(clientId, out var ctx))
            throw new KeyNotFoundException($"Client {clientId} not found.");
        var payload = TelegramSerializer.Serialize(telegram);
        var framed = _framer.Frame(payload);
        await ctx.SendAsync(framed, cancellationToken);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger?.LogError(ex, "Accept error."); break; }

            var ctx = new ClientContext(client, Guid.NewGuid());
            _clients[ctx.Id] = ctx;
            _logger?.LogInformation("Client {Id} connected from {Endpoint}.", ctx.Id, client.Client.RemoteEndPoint);
            _ = HandleClientAsync(ctx, ct);
        }
    }

    private async Task HandleClientAsync(ClientContext ctx, CancellationToken ct)
    {
        var buffer = new byte[65536];
        var accumulated = new System.IO.MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int bytesRead;
                try { bytesRead = await ctx.Stream.ReadAsync(buffer, ct); }
                catch { break; }
                if (bytesRead == 0) break;
                accumulated.Write(buffer, 0, bytesRead);
                ProcessBuffer(accumulated, ctx.Id);
            }
        }
        finally
        {
            _clients.TryRemove(ctx.Id, out _);
            ctx.Dispose();
            _logger?.LogInformation("Client {Id} disconnected.", ctx.Id);
        }
    }

    private void ProcessBuffer(System.IO.MemoryStream accumulated, Guid clientId)
    {
        while (true)
        {
            var data = accumulated.ToArray();
            if (!_framer.TryExtract(data, out var message, out int consumed)) break;
            int remaining = data.Length - consumed;
            accumulated.SetLength(0);
            if (remaining > 0) accumulated.Write(data, consumed, remaining);
            DispatchTelegram(message.ToArray(), clientId);
        }
    }

    private void DispatchTelegram(byte[] payload, Guid clientId)
    {
        foreach (var def in _registry.Definitions)
        {
            if (def.TotalWireSize == payload.Length)
            {
                try
                {
                    var telegram = TelegramSerializer.Deserialize(def, payload);
                    TelegramReceived?.Invoke(this, new TelegramReceivedEventArgs(telegram));
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to deserialize telegram '{Id}' from client {ClientId}.", def.Id, clientId);
                }
            }
        }
        _logger?.LogWarning("No matching telegram definition for payload of {Length} bytes from client {ClientId}.", payload.Length, clientId);
    }

    private sealed class ClientContext : IDisposable
    {
        private readonly TcpClient _client;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public Guid Id { get; }
        public NetworkStream Stream { get; }

        public ClientContext(TcpClient client, Guid id)
        {
            _client = client;
            Id = id;
            Stream = client.GetStream();
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        _cts?.Dispose();
    }
}
