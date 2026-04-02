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
public sealed class TcpPlcClient(
    ConnectionConfiguration config,
    TelegramRegistry registry,
    IMessageFramer? framer = null,
    ILogger<TcpPlcClient>? logger = null,
    ByteOrder byteOrder = ByteOrder.BigEndian) : IPlcConnection
{
    private readonly ConnectionConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly TelegramRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly IMessageFramer _framer = framer ?? new TelegramIdFramer(registry.Definitions, byteOrder);

    // Pre-built dispatch lookup tables (constructed once, used on every received telegram).
    // _idGroups: for each unique (offset, type) combination used as a MessageId discriminator,
    //            holds a dictionary keyed by MessageId value → fast O(1) dispatch.
    // _sizeIndex: for size-based fallback (definitions without a MessageId).
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

        var payload = TelegramSerializer.Serialize(telegram, byteOrder);
        await SendFramedAsync(payload, cancellationToken);
    }

    /// <summary>Serialises and sends a strongly-typed telegram.</summary>
    public async Task SendAsync<T>(T telegram, CancellationToken cancellationToken = default) where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _stream == null)
            throw new InvalidOperationException("Not connected.");

        await SendFramedAsync(telegram.Serialize(byteOrder), cancellationToken);
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
    public IDisposable Subscribe<T>(Action<T> handler) where T : ITypedS7Telegram<T>
    {
        EventHandler<TelegramReceivedEventArgs> listener = (_, e) =>
        {
            var def = T.Definition;
            if (!MatchesMessageId(e.RawPayload, def)) return;
            try { handler(T.Deserialize(e.RawPayload, byteOrder)); }
            catch (Exception ex) { logger?.LogWarning(ex, "Typed handler for {T} threw.", typeof(T).Name); }
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
    public IDisposable Subscribe<T>(Action<T, string, int> handler) where T : ITypedS7Telegram<T>
    {
        EventHandler<TelegramReceivedEventArgs> listener = (_, e) =>
        {
            var def = T.Definition;
            if (!MatchesMessageId(e.RawPayload, def)) return;
            try { handler(T.Deserialize(e.RawPayload, byteOrder), e.RemoteAddress, e.Port); }
            catch (Exception ex) { logger?.LogWarning(ex, "Typed handler for {T} threw.", typeof(T).Name); }
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
                logger?.LogInformation("Connecting to {Host}:{Port}...", _config.Host, _config.Port);
                _client = new TcpClient
                {
                    SendTimeout    = _config.TimeoutMs,
                    ReceiveTimeout = _config.TimeoutMs,
                    NoDelay        = _config.NoDelay,
                };
                if (_config.ReceiveBufferSize > 0) _client.ReceiveBufferSize = _config.ReceiveBufferSize;
                if (_config.SendBufferSize    > 0) _client.SendBufferSize    = _config.SendBufferSize;
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
                logger?.LogWarning(ex, "Connection to {Host}:{Port} failed. Retrying in {Interval} ms.", _config.Host, _config.Port, _config.ReconnectIntervalMs);
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
        var buffer = new byte[65536];
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
                logger?.LogWarning(ex, "Read error on TCP stream.");
                break;
            }

            if (bytesRead == 0)
            {
                logger?.LogInformation("Remote endpoint closed the connection.");
                break;
            }

            // Only materialise a separate copy for the diagnostic event when someone is listening.
            if (RawBytesReceived is { } rawReceived)
            {
                var received = new byte[bytesRead];
                Array.Copy(buffer, received, bytesRead);
                rawReceived.Invoke(this, new RawBytesEventArgs(received, _config.Host, _config.Port));
            }

            accumulated.Write(buffer, 0, bytesRead);
            ProcessBuffer(accumulated);
        }
    }

    private void ProcessBuffer(System.IO.MemoryStream accumulated)
    {
        while (true)
        {
            int dataLength = (int)accumulated.Length;
            if (dataLength == 0) break;

            // Use GetBuffer() to get a span over the internal array without allocating a copy.
            byte[] rawBuffer = accumulated.GetBuffer();
            var data = rawBuffer.AsSpan(0, dataLength);

            if (!_framer.TryExtract(data, out var message, out int consumed))
            {
                if (consumed > 0)
                {
                    // Definite ID mismatch — discard unrecognised byte(s) and keep scanning.
                    logger?.LogWarning("Unrecognised telegram ID in receive buffer; discarding {Count} byte(s).", consumed);
                    int rem = dataLength - consumed;
                    // Shift remaining bytes to the front, then truncate.
                    if (rem > 0)
                    {
                        accumulated.Position = 0;
                        accumulated.Write(rawBuffer, consumed, rem);
                    }
                    accumulated.SetLength(rem);
                    continue;
                }
                break; // Insufficient data — wait for more bytes.
            }

            int remaining = dataLength - consumed;
            // Materialise the framed message into its own array before modifying the MemoryStream.
            var msgArray = message.ToArray();
            if (remaining > 0)
            {
                accumulated.Position = 0;
                accumulated.Write(rawBuffer, consumed, remaining);
            }
            accumulated.SetLength(remaining);

            DispatchTelegram(msgArray, _config.Host, _config.Port);
        }
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    private void DispatchTelegram(byte[] payload, string remoteAddress, int port)
    {
        // 1. MessageId-based dispatch — O(1) per (offset, type) group.
        //    In the common case (all definitions use the same offset/type) this is a single hash lookup.
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
        logger?.LogWarning("No matching telegram definition for payload of {Length} bytes.", payload.Length);
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
            logger?.LogWarning("Telegram '{Id}': length field at offset {Offset} extends beyond payload ({PayloadLen} bytes).", def.Id, def.LengthByteOffset, payload.Length);
            UnknownTelegramReceived?.Invoke(this, new UnknownTelegramEventArgs(payload, byteOrder));
            return false;
        }

        long receivedLength = TelegramIdFramer.ReadLength(payload, def.LengthByteOffset, def.LengthDataType, byteOrder);
        if (receivedLength != def.ConfiguredWireSize)
        {
            logger?.LogWarning("Telegram '{Id}': length field mismatch — expected {Expected}, got {Received}.", def.Id, def.ConfiguredWireSize, receivedLength);
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
            logger?.LogWarning(ex, "Failed to deserialize telegram '{Id}'.", def.Id);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when the id value read from <paramref name="payload"/> at
    /// <see cref="TelegramDefinition.MessageIdByteOffset"/> (using
    /// <see cref="TelegramDefinition.MessageIdDataType"/>) equals
    /// <see cref="TelegramDefinition.MessageId"/>.
    /// </summary>
    private bool MatchesMessageId(byte[] payload, TelegramDefinition def)
    {
        int idSize = S7TypeConverter.GetWireSize(def.MessageIdDataType);
        int minBytes = def.MessageIdByteOffset + idSize;
        if (payload.Length < minBytes) return false;

        long actual = TelegramIdFramer.ReadId(payload, def.MessageIdByteOffset, def.MessageIdDataType, byteOrder);
        return actual == def.MessageId;
    }

    private void SetConnected(bool connected, string reason)
    {
        _isConnected = connected;
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(connected, reason));
        logger?.LogInformation("Connection state: {State} ({Reason})", connected ? "Connected" : "Disconnected", reason);
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
