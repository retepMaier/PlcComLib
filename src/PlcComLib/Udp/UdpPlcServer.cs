using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.Telegrams;

namespace PlcComLib.Udp;

/// <summary>
/// UDP server that listens on a local endpoint and dispatches incoming datagrams. Thread-safe.
/// </summary>
public sealed class UdpPlcServer : IPlcConnection
{
    private readonly ConnectionConfiguration _config;
    private readonly TelegramRegistry _registry;
    private readonly ILogger<UdpPlcServer>? _logger;

    private UdpClient? _udpClient;
    private IPEndPoint? _lastSenderEndpoint;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;
    private bool _disposed;

    public event EventHandler<TelegramReceivedEventArgs>? TelegramReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public bool IsConnected => _isConnected;

    public UdpPlcServer(
        ConnectionConfiguration config,
        TelegramRegistry registry,
        ILogger<UdpPlcServer>? logger = null)
    {
        _config   = config   ?? throw new ArgumentNullException(nameof(config));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger   = logger;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var endpoint = new IPEndPoint(IPAddress.Parse(_config.Host), _config.Port);
        _udpClient = new UdpClient(endpoint);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isConnected = true;
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(true, "UDP server started"));
        _logger?.LogInformation("UDP server listening on {Host}:{Port}", _config.Host, _config.Port);
        _receiveTask = ReceiveLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null) await _cts.CancelAsync();
        if (_receiveTask != null)
            try { await _receiveTask.WaitAsync(cancellationToken); } catch { /* ignore */ }
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
        _isConnected = false;
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(false, "UDP server stopped"));
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    /// <summary>Sends a legacy untyped telegram to the last sender (reply).</summary>
    public async Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        if (_lastSenderEndpoint == null)          throw new InvalidOperationException("No sender endpoint available yet.");
        await SendToEndpointAsync(TelegramSerializer.Serialize(telegram), _lastSenderEndpoint, cancellationToken);
    }

    /// <summary>Sends a strongly-typed telegram to the last sender (reply).</summary>
    public async Task SendAsync<T>(T telegram, CancellationToken cancellationToken = default)
        where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        if (_lastSenderEndpoint == null)          throw new InvalidOperationException("No sender endpoint available yet.");
        await SendToEndpointAsync(telegram.Serialize(), _lastSenderEndpoint, cancellationToken);
    }

    /// <summary>Sends a legacy untyped telegram to a specific endpoint.</summary>
    public async Task SendToAsync(IPEndPoint endpoint, Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        await SendToEndpointAsync(TelegramSerializer.Serialize(telegram), endpoint, cancellationToken);
    }

    /// <summary>Sends a strongly-typed telegram to a specific endpoint.</summary>
    public async Task SendToAsync<T>(IPEndPoint endpoint, T telegram, CancellationToken cancellationToken = default)
        where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        await SendToEndpointAsync(telegram.Serialize(), endpoint, cancellationToken);
    }

    private async Task SendToEndpointAsync(byte[] payload, IPEndPoint endpoint, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try   { await _udpClient!.SendAsync(payload, endpoint, ct); }
        finally { _sendLock.Release(); }
    }

    // ── Typed subscription ────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to <see cref="TelegramReceived"/> and invokes <paramref name="handler"/>
    /// whenever a payload whose MessageId matches <typeparamref name="T"/>.<c>MessageId</c> arrives.
    /// Uses static abstract interface members — zero reflection.
    /// </summary>
    public IDisposable Subscribe<T>(Action<T> handler)
        where T : ITypedS7Telegram<T>
    {
        EventHandler<TelegramReceivedEventArgs> listener = (_, e) =>
        {
            if (e.RawPayload.Length < 2) return;
            if (BinaryPrimitives.ReadUInt16BigEndian(e.RawPayload) != T.MessageId) return;
            try   { handler(T.Deserialize(e.RawPayload)); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Typed handler for {T} threw.", typeof(T).Name); }
        };
        TelegramReceived += listener;
        return new Subscription(() => TelegramReceived -= listener);
    }

    // ── Receive loop ──────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _udpClient != null)
        {
            UdpReceiveResult result;
            try { result = await _udpClient.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "UDP receive error.");
                break;
            }
            _lastSenderEndpoint = result.RemoteEndPoint;
            DispatchTelegram(result.Buffer);
        }
    }

    private void DispatchTelegram(byte[] payload)
    {
        // 1. MessageId-based dispatch
        if (payload.Length >= 2)
        {
            ushort msgId = BinaryPrimitives.ReadUInt16BigEndian(payload);
            foreach (var def in _registry.Definitions)
            {
                if (def.MessageId != 0 && def.MessageId == msgId)
                {
                    TryDeserializeAndFire(def, payload);
                    return;
                }
            }
        }

        // 2. Size-based fallback
        foreach (var def in _registry.Definitions)
        {
            if (def.MessageId == 0 && def.TotalWireSize == payload.Length)
            {
                TryDeserializeAndFire(def, payload);
                return;
            }
        }

        _logger?.LogWarning("No matching UDP telegram definition for payload of {Length} bytes.", payload.Length);
    }

    private void TryDeserializeAndFire(TelegramDefinition def, byte[] payload)
    {
        try
        {
            var telegram = TelegramSerializer.Deserialize(def, payload);
            TelegramReceived?.Invoke(this, new TelegramReceivedEventArgs(telegram, payload));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to deserialize UDP telegram '{Id}'.", def.Id);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        _sendLock.Dispose();
        _cts?.Dispose();
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
