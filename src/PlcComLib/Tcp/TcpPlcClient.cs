using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Telegrams;

namespace PlcComLib.Tcp;

/// <summary>
/// TCP client that connects to a PLC (or any compatible endpoint).
/// Supports automatic reconnection, message framing, typed telegrams, and full-duplex communication.
/// This class is thread-safe.
/// </summary>
public sealed class TcpPlcClient : IPlcConnection
{
    private readonly ConnectionConfiguration _config;
    private readonly TelegramRegistry _registry;
    private readonly IMessageFramer _framer;
    private readonly ILogger<TcpPlcClient>? _logger;
    private readonly ByteOrder _byteOrder;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;
    private bool _disposed;

    public event EventHandler<TelegramReceivedEventArgs>? TelegramReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// Fired when a received payload cannot be matched to any registered telegram definition.
    /// Subscribe to inspect or log unrecognised messages.
    /// </summary>
    public event EventHandler<UnknownTelegramEventArgs>? UnknownTelegramReceived;

    /// <summary>Fired immediately after raw bytes are written to the network.</summary>
    public event EventHandler<RawBytesEventArgs>? RawBytesSent;

    /// <summary>Fired immediately after raw bytes are read from the network.</summary>
    public event EventHandler<RawBytesEventArgs>? RawBytesReceived;

    public bool IsConnected => _isConnected;

    public TcpPlcClient(
        ConnectionConfiguration config,
        TelegramRegistry registry,
        IMessageFramer? framer = null,
        ILogger<TcpPlcClient>? logger = null,
        ByteOrder byteOrder = ByteOrder.BigEndian)
    {
        _config    = config   ?? throw new ArgumentNullException(nameof(config));
        _registry  = registry ?? throw new ArgumentNullException(nameof(registry));
        _framer    = framer   ?? new TelegramIdFramer(registry.Definitions, byteOrder);
        _logger    = logger;
        _byteOrder = byteOrder;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = ConnectLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null) await _cts.CancelAsync();
        if (_receiveTask != null)
            try { await _receiveTask.WaitAsync(cancellationToken); } catch { /* ignore */ }
        CloseConnection("Stopped");
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    /// <summary>Serialises and sends a legacy untyped telegram.</summary>
    public async Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _stream == null)
            throw new InvalidOperationException("Not connected.");

        var payload = TelegramSerializer.Serialize(telegram, _byteOrder);
        await SendFramedAsync(payload, cancellationToken);
    }

    /// <summary>Serialises and sends a strongly-typed telegram.</summary>
    public async Task SendAsync<T>(T telegram, CancellationToken cancellationToken = default)
        where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _stream == null)
            throw new InvalidOperationException("Not connected.");

        await SendFramedAsync(telegram.Serialize(_byteOrder), cancellationToken);
    }

    private async Task SendFramedAsync(byte[] payload, CancellationToken ct)
    {
        var framed = _framer.Frame(payload);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _stream!.WriteAsync(framed, ct);
            await _stream!.FlushAsync(ct);
            RawBytesSent?.Invoke(this, new RawBytesEventArgs(framed, _config.Host, _config.Port));
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ── Typed subscription ────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to <see cref="TelegramReceived"/> and invokes <paramref name="handler"/>
    /// whenever a payload whose MessageId matches <typeparamref name="T"/>.<c>MessageId</c> arrives.
    /// Uses static abstract interface members — zero reflection.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> that unsubscribes when disposed.</returns>
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
    /// whenever a payload whose MessageId matches <typeparamref name="T"/>.<c>MessageId</c> arrives.
    /// The handler receives the deserialised telegram plus the remote IP address and port.
    /// Uses static abstract interface members — zero reflection.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> that unsubscribes when disposed.</returns>
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

    // ── Connection loop ───────────────────────────────────────────────────────

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _logger?.LogInformation("Connecting to {Host}:{Port}...", _config.Host, _config.Port);
                _client = new TcpClient();
                _client.SendTimeout    = _config.TimeoutMs;
                _client.ReceiveTimeout = _config.TimeoutMs;
                await _client.ConnectAsync(_config.Host, _config.Port, ct);
                _stream = _client.GetStream();
                SetConnected(true, "Connected");
                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Connection to {Host}:{Port} failed. Retrying in {Interval} ms.",
                    _config.Host, _config.Port, _config.ReconnectIntervalMs);
            }
            finally
            {
                CloseConnection("Disconnected");
            }

            if (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(_config.ReconnectIntervalMs, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer      = new byte[65536];
        var accumulated = new System.IO.MemoryStream();

        while (!ct.IsCancellationRequested && _stream != null)
        {
            int bytesRead;
            try
            {
                bytesRead = await _stream.ReadAsync(buffer, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Read error on TCP stream.");
                break;
            }

            if (bytesRead == 0)
            {
                _logger?.LogInformation("Remote endpoint closed the connection.");
                break;
            }

            var received = new byte[bytesRead];
            Array.Copy(buffer, received, bytesRead);
            RawBytesReceived?.Invoke(this, new RawBytesEventArgs(received, _config.Host, _config.Port));

            accumulated.Write(buffer, 0, bytesRead);
            ProcessBuffer(accumulated);
        }
    }

    private void ProcessBuffer(System.IO.MemoryStream accumulated)
    {
        while (true)
        {
            var data = accumulated.ToArray();
            if (!_framer.TryExtract(data, out var message, out int consumed)) break;

            int remaining = data.Length - consumed;
            accumulated.SetLength(0);
            if (remaining > 0) accumulated.Write(data, consumed, remaining);

            DispatchTelegram(message.ToArray(), _config.Host, _config.Port);
        }
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    private void DispatchTelegram(byte[] payload, string remoteAddress, int port)
    {
        // 1. MessageId-based dispatch (typed telegrams with MessageId > 0)
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

        // 2. Size-based fallback (legacy definitions without a MessageId)
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
            "No matching telegram definition for payload of {Length} bytes (candidate TelegramId=0x{Id:X4}).",
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
            _logger?.LogWarning(ex, "Failed to deserialize telegram '{Id}'.", def.Id);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ushort ReadTelegramId(ReadOnlySpan<byte> data) =>
        _byteOrder == ByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data)
            : BinaryPrimitives.ReadUInt16BigEndian(data);

    private void SetConnected(bool connected, string reason)
    {
        _isConnected = connected;
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(connected, reason));
        _logger?.LogInformation("Connection state: {State} ({Reason})",
            connected ? "Connected" : "Disconnected", reason);
    }

    private void CloseConnection(string reason)
    {
        if (_isConnected) SetConnected(false, reason);
        try { _stream?.Dispose(); } catch { /* ignore */ }
        try { _client?.Dispose(); } catch { /* ignore */ }
        _stream = null;
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        _sendLock.Dispose();
        _cts?.Dispose();
    }

    // ── Private subscription handle ───────────────────────────────────────────

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
