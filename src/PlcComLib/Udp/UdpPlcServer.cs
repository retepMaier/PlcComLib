using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.DataTypes;
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
    private readonly ByteOrder _byteOrder;

    private UdpClient? _udpClient;
    private IPEndPoint? _lastSenderEndpoint;
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

    /// <summary>Fired immediately after raw bytes are sent over UDP.</summary>
    public event EventHandler<RawBytesEventArgs>? RawBytesSent;

    /// <summary>Fired immediately after raw bytes are received over UDP.</summary>
    public event EventHandler<RawBytesEventArgs>? RawBytesReceived;

    public bool IsConnected => _isConnected;

    public UdpPlcServer(
        ConnectionConfiguration config,
        TelegramRegistry registry,
        ILogger<UdpPlcServer>? logger = null,
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
        await SendToEndpointAsync(TelegramSerializer.Serialize(telegram, _byteOrder), _lastSenderEndpoint, cancellationToken);
    }

    /// <summary>Sends a strongly-typed telegram to the last sender (reply).</summary>
    public async Task SendAsync<T>(T telegram, CancellationToken cancellationToken = default)
        where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        if (_lastSenderEndpoint == null)          throw new InvalidOperationException("No sender endpoint available yet.");
        await SendToEndpointAsync(telegram.Serialize(_byteOrder), _lastSenderEndpoint, cancellationToken);
    }

    /// <summary>Sends a legacy untyped telegram to a specific endpoint.</summary>
    public async Task SendToAsync(IPEndPoint endpoint, Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        await SendToEndpointAsync(TelegramSerializer.Serialize(telegram, _byteOrder), endpoint, cancellationToken);
    }

    /// <summary>Sends a strongly-typed telegram to a specific endpoint.</summary>
    public async Task SendToAsync<T>(IPEndPoint endpoint, T telegram, CancellationToken cancellationToken = default)
        where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        await SendToEndpointAsync(telegram.Serialize(_byteOrder), endpoint, cancellationToken);
    }

    private async Task SendToEndpointAsync(byte[] payload, IPEndPoint endpoint, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await _udpClient!.SendAsync(payload, endpoint, ct);
            RawBytesSent?.Invoke(this, new RawBytesEventArgs(payload, endpoint.Address.ToString(), endpoint.Port));
        }
        finally { _sendLock.Release(); }
    }

    // ── Typed subscription ────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to <see cref="TelegramReceived"/> and invokes <paramref name="handler"/>
    /// whenever a datagram whose MessageId matches <typeparamref name="T"/>.<c>MessageId</c> arrives.
    /// Uses static abstract interface members — zero reflection.
    /// </summary>
    public IDisposable Subscribe<T>(Action<T> handler)
        where T : ITypedS7Telegram<T>
    {
        EventHandler<TelegramReceivedEventArgs> listener = (_, e) =>
        {
            if (e.RawPayload.Length < 2) return;
            if (ReadTelegramId(e.RawPayload) != T.Definition.MessageId) return;
            try   { handler(T.Deserialize(e.RawPayload, _byteOrder)); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Typed handler for {T} threw.", typeof(T).Name); }
        };
        TelegramReceived += listener;
        return new Subscription(() => TelegramReceived -= listener);
    }

    /// <summary>
    /// Subscribes to <see cref="TelegramReceived"/> and invokes <paramref name="handler"/>
    /// whenever a datagram whose MessageId matches <typeparamref name="T"/>.<c>MessageId</c> arrives.
    /// The handler receives the deserialised telegram plus the remote IP address and port.
    /// Uses static abstract interface members — zero reflection.
    /// </summary>
    public IDisposable Subscribe<T>(Action<T, string, int> handler)
        where T : ITypedS7Telegram<T>
    {
        EventHandler<TelegramReceivedEventArgs> listener = (_, e) =>
        {
            if (e.RawPayload.Length < 2) return;
            if (ReadTelegramId(e.RawPayload) != T.Definition.MessageId) return;
            try   { handler(T.Deserialize(e.RawPayload, _byteOrder), e.RemoteAddress, e.Port); }
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
            string remoteAddress = result.RemoteEndPoint.Address.ToString();
            int    remotePort    = result.RemoteEndPoint.Port;
            RawBytesReceived?.Invoke(this, new RawBytesEventArgs(result.Buffer, remoteAddress, remotePort));
            DispatchTelegram(result.Buffer, remoteAddress, remotePort);
        }
    }

    private void DispatchTelegram(byte[] payload, string remoteAddress, int port)
    {
        // 1. MessageId-based dispatch
        if (payload.Length >= 2)
        {
            ushort msgId = ReadTelegramId(payload);
            foreach (var def in _registry.Definitions)
            {
                if (def.MessageId != 0 && def.MessageId == msgId)
                {
                    TryDeserializeAndFire(def, payload, remoteAddress, port);
                    return;
                }
            }
        }

        // 2. Size-based fallback
        foreach (var def in _registry.Definitions)
        {
            if (def.MessageId == 0 && def.EffectiveWireSize == payload.Length)
            {
                TryDeserializeAndFire(def, payload, remoteAddress, port);
                return;
            }
        }

        // 3. No match — raise UnknownTelegramReceived
        _logger?.LogWarning(
            "No matching UDP telegram definition for payload of {Length} bytes (candidate TelegramId=0x{Id:X4}).",
            payload.Length,
            payload.Length >= 2 ? ReadTelegramId(payload) : 0);
        UnknownTelegramReceived?.Invoke(this, new UnknownTelegramEventArgs(payload, _byteOrder));
    }

    private void TryDeserializeAndFire(TelegramDefinition def, byte[] payload, string remoteAddress, int port)
    {
        try
        {
            var telegram = TelegramSerializer.Deserialize(def, payload, _byteOrder);
            TelegramReceived?.Invoke(this, new TelegramReceivedEventArgs(telegram, payload, remoteAddress, port));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to deserialize UDP telegram '{Id}'.", def.Id);
        }
    }

    private ushort ReadTelegramId(ReadOnlySpan<byte> data) =>
        _byteOrder == ByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data)
            : BinaryPrimitives.ReadUInt16BigEndian(data);

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
