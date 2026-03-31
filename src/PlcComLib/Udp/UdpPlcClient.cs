using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.Udp;

/// <summary>
/// UDP client for sending telegrams to a remote endpoint and receiving datagrams. Thread-safe.
/// </summary>
public sealed class UdpPlcClient : IPlcConnection
{
    private readonly ConnectionConfiguration _config;
    private readonly TelegramRegistry _registry;
    private readonly ILogger<UdpPlcClient>? _logger;
    private readonly ByteOrder _byteOrder;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;
    private bool _disposed;

    public event EventHandler<TelegramReceivedEventArgs>? TelegramReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// Fired when a received datagram cannot be matched to any registered telegram definition.
    /// Subscribe to inspect or log unrecognised messages.
    /// </summary>
    public event EventHandler<UnknownTelegramEventArgs>? UnknownTelegramReceived;

    public bool IsConnected => _isConnected;

    public UdpPlcClient(
        ConnectionConfiguration config,
        TelegramRegistry registry,
        ILogger<UdpPlcClient>? logger = null,
        ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        _config    = config   ?? throw new ArgumentNullException(nameof(config));
        _registry  = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger    = logger;
        _byteOrder = byteOrder;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _udpClient = new UdpClient();
        _udpClient.Client.SendTimeout    = _config.TimeoutMs;
        _udpClient.Client.ReceiveTimeout = _config.TimeoutMs;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isConnected = true;
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(true, "UDP client started"));
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
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(false, "UDP client stopped"));
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    /// <summary>Serialises and sends a legacy untyped telegram.</summary>
    public async Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null)
            throw new InvalidOperationException("UDP client is not started.");
        await SendPayloadAsync(TelegramSerializer.Serialize(telegram, _byteOrder), cancellationToken);
    }

    /// <summary>Serialises and sends a strongly-typed telegram.</summary>
    public async Task SendAsync<T>(T telegram, CancellationToken cancellationToken = default)
        where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null)
            throw new InvalidOperationException("UDP client is not started.");
        await SendPayloadAsync(telegram.Serialize(_byteOrder), cancellationToken);
    }

    private async Task SendPayloadAsync(byte[] payload, CancellationToken ct)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(_config.Host), _config.Port);
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
            try   { handler(T.Deserialize(e.RawPayload, _byteOrder)); }
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

        // 3. No match — raise UnknownTelegramReceived
        _logger?.LogWarning(
            "No matching UDP telegram definition for payload of {Length} bytes (candidate TelegramId=0x{Id:X4}).",
            payload.Length,
            payload.Length >= 2 ? (ushort)((payload[0] << 8) | payload[1]) : 0);
        UnknownTelegramReceived?.Invoke(this, new UnknownTelegramEventArgs(payload));
    }

    private void TryDeserializeAndFire(TelegramDefinition def, byte[] payload)
    {
        try
        {
            var telegram = TelegramSerializer.Deserialize(def, payload, _byteOrder);
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
