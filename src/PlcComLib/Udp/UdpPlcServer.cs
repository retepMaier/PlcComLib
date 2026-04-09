using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.Core.Events;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Telegrams;

namespace PlcComLib.Udp;

/// <summary>
/// UDP server that listens on a local endpoint and dispatches incoming datagrams. Thread-safe.
/// </summary>
public sealed partial class UdpPlcServer(
    ConnectionConfiguration config,
    TelegramRegistry registry,
    ILogger<UdpPlcServer>? logger = null,
    ByteOrder byteOrder = ByteOrder.BigEndian) : IPlcConnection
{
    private readonly ConnectionConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

    // Pre-built dispatch lookup tables — see TcpPlcClient for rationale.
    private readonly (int Offset, S7DataType Type, Dictionary<long, TelegramDefinition> Lookup)[] _idGroups =
        registry!.Definitions
            .Where(d => d.MessageId != 0)
            .GroupBy(d => (d.MessageIdByteOffset, d.MessageIdDataType))
            .Select(g => (g.Key.MessageIdByteOffset, g.Key.MessageIdDataType, g.ToDictionary(d => d.MessageId)))
            .ToArray();

    private readonly Dictionary<int, TelegramDefinition> _sizeIndex =
        registry!.Definitions
            .Where(d => d.MessageId == 0 && d.EffectiveWireSize > 0)
            .GroupBy(d => d.EffectiveWireSize)
            .ToDictionary(g => g.Key, g => g.First());

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

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var endpoint = new IPEndPoint(IPAddress.Parse(_config.Host), _config.Port);
        _udpClient = new UdpClient(endpoint);
        if (_config.ReceiveBufferSize > 0) _udpClient.Client.ReceiveBufferSize = _config.ReceiveBufferSize;
        if (_config.SendBufferSize    > 0) _udpClient.Client.SendBufferSize    = _config.SendBufferSize;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isConnected = true;
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(true, "UDP server started"));
        LogServerListening(logger, _config.Host, _config.Port);
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
        await SendToEndpointAsync(TelegramSerializer.Serialize(telegram, byteOrder), _lastSenderEndpoint, cancellationToken);
    }

    /// <summary>Sends a strongly-typed telegram to the last sender (reply).</summary>
    public async Task SendAsync<T>(T telegram, CancellationToken cancellationToken = default)where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        if (_lastSenderEndpoint == null)          throw new InvalidOperationException("No sender endpoint available yet.");
        await SendToEndpointAsync(telegram.Serialize(byteOrder), _lastSenderEndpoint, cancellationToken);
    }

    /// <summary>Sends a legacy untyped telegram to a specific endpoint.</summary>
    public async Task SendToAsync(IPEndPoint endpoint, Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        await SendToEndpointAsync(TelegramSerializer.Serialize(telegram, byteOrder), endpoint, cancellationToken);
    }

    /// <summary>Sends a legacy untyped telegram to a specific remote address and port.</summary>
    public async Task SendToAsync(string remoteAddress, int port, Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        if (!IPAddress.TryParse(remoteAddress, out var ipAddress))
            throw new ArgumentException($"Invalid IP address: '{remoteAddress}'.", nameof(remoteAddress));
        await SendToEndpointAsync(TelegramSerializer.Serialize(telegram, byteOrder), new IPEndPoint(ipAddress, port), cancellationToken);
    }

    /// <summary>Sends a strongly-typed telegram to a specific endpoint.</summary>
    public async Task SendToAsync<T>(IPEndPoint endpoint, T telegram, CancellationToken cancellationToken = default)where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        await SendToEndpointAsync(telegram.Serialize(byteOrder), endpoint, cancellationToken);
    }

    /// <summary>Sends a strongly-typed telegram to a specific remote address and port.</summary>
    public async Task SendToAsync<T>(string remoteAddress, int port, T telegram, CancellationToken cancellationToken = default)where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        if (!IPAddress.TryParse(remoteAddress, out var ipAddress))
            throw new ArgumentException($"Invalid IP address: '{remoteAddress}'.", nameof(remoteAddress));
        await SendToEndpointAsync(telegram.Serialize(byteOrder), new IPEndPoint(ipAddress, port), cancellationToken);
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
    public IDisposable Subscribe<T>(Action<T> handler)where T : ITypedS7Telegram<T>
    {
        EventHandler<TelegramReceivedEventArgs> listener = (_, e) =>
        {
            var def = T.Definition;
            if (!MatchesMessageId(e.RawPayload, def)) return;
            try   { handler(T.Deserialize(e.RawPayload, byteOrder)); }
            catch (Exception ex) { LogTypedHandlerThrew(logger, ex, typeof(T).Name); }
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
    public IDisposable Subscribe<T>(Action<T, string, int> handler)where T : ITypedS7Telegram<T>
    {
        EventHandler<TelegramReceivedEventArgs> listener = (_, e) =>
        {
            var def = T.Definition;
            if (!MatchesMessageId(e.RawPayload, def)) return;
            try   { handler(T.Deserialize(e.RawPayload, byteOrder), e.RemoteAddress, e.Port); }
            catch (Exception ex) { LogTypedHandlerThrew(logger, ex, typeof(T).Name); }
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
                LogUdpReceiveError(logger, ex);
                break;
            }
            _lastSenderEndpoint = result.RemoteEndPoint;
            // Only call ToString() when someone is actually listening — avoids a heap allocation per datagram.
            bool hasSubscribers = RawBytesReceived is not null || TelegramReceived is not null || UnknownTelegramReceived is not null;
            string remoteAddress = hasSubscribers
                ? result.RemoteEndPoint.Address.ToString()
                : string.Empty;
            int    remotePort    = result.RemoteEndPoint.Port;
            RawBytesReceived?.Invoke(this, new RawBytesEventArgs(result.Buffer, remoteAddress, remotePort));
            DispatchTelegram(result.Buffer, remoteAddress, remotePort);
        }
    }

    private void DispatchTelegram(byte[] payload, string remoteAddress, int port)
    {
        // 1. MessageId-based dispatch — O(1) per (offset, type) group.
        foreach (var (offset, type, lookup) in _idGroups)
        {
            int idSize = S7TypeConverter.GetWireSize(type);
            if (payload.Length < offset + idSize) continue;
            long id = TelegramIdFramer.ReadId(payload, offset, type, byteOrder);
            if (!lookup.TryGetValue(id, out var def)) continue;

            if (!ValidateLength(def, payload)) return;
            TryDeserializeAndFire(def, payload, remoteAddress, port);
            return;
        }

        // 2. Size-based fallback — O(1) dictionary lookup.
        if (_sizeIndex.TryGetValue(payload.Length, out var sizeDef))
        {
            if (!ValidateLength(sizeDef, payload)) return;
            TryDeserializeAndFire(sizeDef, payload, remoteAddress, port);
            return;
        }

        // 3. No match — raise UnknownTelegramReceived
        LogNoMatchingUdpDefinition(logger, payload.Length);
        UnknownTelegramReceived?.Invoke(this, new UnknownTelegramEventArgs(payload, byteOrder));
    }

    /// <summary>
    /// Validates the embedded length field (if configured via
    /// <c>.WithLength&lt;TType&gt;(length, byteOffset)</c>).
    /// Returns <c>true</c> when valid (or when no length field is configured).
    /// Returns <c>false</c> and raises <see cref="UnknownTelegramReceived"/> on mismatch.
    /// </summary>
    private bool ValidateLength(TelegramDefinition def, byte[] payload)
    {
        if (def.LengthByteOffset < 0) return true;

        int fieldEnd = def.LengthByteOffset + S7TypeConverter.GetWireSize(def.LengthDataType);
        if (payload.Length < fieldEnd)
        {
            LogUdpLengthFieldBeyondPayload(logger, def.Id, def.LengthByteOffset, payload.Length);
            UnknownTelegramReceived?.Invoke(this, new UnknownTelegramEventArgs(payload, byteOrder));
            return false;
        }

        long receivedLength = TelegramIdFramer.ReadLength(payload, def.LengthByteOffset, def.LengthDataType, byteOrder);
        if (receivedLength != def.ConfiguredWireSize)
        {
            LogUdpLengthFieldMismatch(logger, def.Id, def.ConfiguredWireSize, receivedLength);
            UnknownTelegramReceived?.Invoke(this, new UnknownTelegramEventArgs(payload, byteOrder));
            return false;
        }

        return true;
    }

    private void TryDeserializeAndFire(TelegramDefinition def, byte[] payload, string remoteAddress, int port)
    {
        try
        {
            var telegram = TelegramSerializer.Deserialize(def, payload, byteOrder);
            TelegramReceived?.Invoke(this, new TelegramReceivedEventArgs(telegram, payload, remoteAddress, port));
        }
        catch (Exception ex)
        {
            LogUdpDeserializeFailed(logger, ex, def.Id);
        }
    }

    private bool MatchesMessageId(byte[] payload, TelegramDefinition def)
    {
        int idSize   = S7TypeConverter.GetWireSize(def.MessageIdDataType);
        int minBytes = def.MessageIdByteOffset + idSize;
        if (payload.Length < minBytes) return false;

        long actual = TelegramIdFramer.ReadId(payload, def.MessageIdByteOffset, def.MessageIdDataType, byteOrder);
        return actual == def.MessageId;
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
