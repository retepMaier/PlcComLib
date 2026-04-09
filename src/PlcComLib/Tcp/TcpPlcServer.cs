using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PlcComLib.Core;
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
    private readonly ConnectionConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly TelegramRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly IMessageFramer _framer =  new TelegramIdFramer(registry.Definitions, byteOrder);

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
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(true, "Server started"));
        LogServerListening(logger, _config.Host, _config.Port);
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

    // ── Send (broadcast) ──────────────────────────────────────────────────────

    /// <summary>Broadcasts a legacy untyped telegram to all connected clients.</summary>
    public async Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var framed = _framer.Frame(TelegramSerializer.Serialize(telegram, byteOrder));
        await Task.WhenAll(_clients.Values.Select(async c =>
        {
            await c.SendAsync(framed, cancellationToken);
            RawBytesSent?.Invoke(this, new RawBytesEventArgs(framed, c.RemoteAddress, c.Port));
        }));
    }

    /// <summary>Broadcasts a strongly-typed telegram to all connected clients.</summary>
    public async Task SendAsync<T>(T telegram, CancellationToken cancellationToken = default)where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var framed = _framer.Frame(telegram.Serialize(byteOrder));
        await Task.WhenAll(_clients.Values.Select(async c =>
        {
            await c.SendAsync(framed, cancellationToken);
            RawBytesSent?.Invoke(this, new RawBytesEventArgs(framed, c.RemoteAddress, c.Port));
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
        RawBytesSent?.Invoke(this, new RawBytesEventArgs(framed, ctx.RemoteAddress, ctx.Port));
    }

    /// <summary>Sends a legacy untyped telegram to a specific client identified by remote IP address and port.</summary>
    public async Task SendToAsync(string remoteAddress, int port, Telegram telegram, CancellationToken cancellationToken = default)
    {
        var ctx = FindClient(remoteAddress, port);
        var framed = _framer.Frame(TelegramSerializer.Serialize(telegram, byteOrder));
        await ctx.SendAsync(framed, cancellationToken);
        RawBytesSent?.Invoke(this, new RawBytesEventArgs(framed, ctx.RemoteAddress, ctx.Port));
    }

    /// <summary>Sends a strongly-typed telegram to a specific client.</summary>
    public async Task SendToAsync<T>(Guid clientId, T telegram, CancellationToken cancellationToken = default)where T : ITypedS7Telegram<T>
    {
        if (!_clients.TryGetValue(clientId, out var ctx))
            throw new KeyNotFoundException($"Client {clientId} not found.");
        var framed = _framer.Frame(telegram.Serialize(byteOrder));
        await ctx.SendAsync(framed, cancellationToken);
        RawBytesSent?.Invoke(this, new RawBytesEventArgs(framed, ctx.RemoteAddress, ctx.Port));
    }

    /// <summary>Sends a strongly-typed telegram to a specific client identified by remote IP address and port.</summary>
    public async Task SendToAsync<T>(string remoteAddress, int port, T telegram, CancellationToken cancellationToken = default)where T : ITypedS7Telegram<T>
    {
        var ctx = FindClient(remoteAddress, port);
        var framed = _framer.Frame(telegram.Serialize(byteOrder));
        await ctx.SendAsync(framed, cancellationToken);
        RawBytesSent?.Invoke(this, new RawBytesEventArgs(framed, ctx.RemoteAddress, ctx.Port));
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
    /// Subscribes to <see cref="TelegramReceived"/> and invokes <paramref name="handler"/>
    /// whenever a payload whose MessageId matches <typeparamref name="T"/>.<c>MessageId</c> arrives.
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
    /// whenever a payload whose MessageId matches <typeparamref name="T"/>.<c>MessageId</c> arrives.
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

    // ── Accept / receive loops ────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LogAcceptError(logger, ex); break; }

            // Apply low-latency socket options to each accepted connection.
            client.NoDelay = _config.NoDelay;
            if (_config.ReceiveBufferSize > 0) client.ReceiveBufferSize = _config.ReceiveBufferSize;
            if (_config.SendBufferSize    > 0) client.SendBufferSize    = _config.SendBufferSize;

            var ctx = new ClientContext(client, Guid.NewGuid());
            _clients[ctx.Id] = ctx;
            LogClientConnected(logger, ctx.Id, client.Client.RemoteEndPoint);

            // Run the per-client handler without awaiting, but ensure unhandled exceptions are logged.
            _ = HandleClientAsync(ctx, ct).ContinueWith(
                t => LogUnhandledClientException(logger, t.Exception, ctx.Id),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }

    private async Task HandleClientAsync(ClientContext ctx, CancellationToken ct)
    {
        var buffer      = new byte[65536];
        var accumulated = new System.IO.MemoryStream();
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
                {
                    var received = new byte[bytesRead];
                    Array.Copy(buffer, received, bytesRead);
                    rawReceived.Invoke(this, new RawBytesEventArgs(received, ctx.RemoteAddress, ctx.Port));
                }
                accumulated.Write(buffer, 0, bytesRead);
                ProcessBuffer(accumulated, ctx);
            }
        }
        finally
        {
            _clients.TryRemove(ctx.Id, out _);
            ctx.Dispose();
            LogClientDisconnected(logger, ctx.Id);
        }
    }

    private void ProcessBuffer(System.IO.MemoryStream accumulated, ClientContext ctx)
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
                    LogUnrecognisedTelegramId(logger, consumed);
                    int rem = dataLength - consumed;
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
            var msgArray = message.ToArray();
            if (remaining > 0)
            {
                accumulated.Position = 0;
                accumulated.Write(rawBuffer, consumed, remaining);
            }
            accumulated.SetLength(remaining);

            DispatchTelegram(msgArray, ctx);
        }
    }

    private void DispatchTelegram(byte[] payload, ClientContext ctx)
    {
        // 1. MessageId-based dispatch — O(1) per (offset, type) group.
        foreach (var (offset, type, lookup) in _idGroups)
        {
            int idSize = S7TypeConverter.GetWireSize(type);
            if (payload.Length < offset + idSize) continue;
            long id = TelegramIdFramer.ReadId(payload, offset, type, byteOrder);
            if (!lookup.TryGetValue(id, out var def)) continue;

            if (!ValidateLength(def, payload, ctx)) return;
            TryDeserializeAndFire(def, payload, ctx);
            return;
        }

        // 2. Size-based fallback — O(1) dictionary lookup.
        if (_sizeIndex.TryGetValue(payload.Length, out var sizeDef))
        {
            if (!ValidateLength(sizeDef, payload, ctx)) return;
            TryDeserializeAndFire(sizeDef, payload, ctx);
            return;
        }

        // 3. No match — raise UnknownTelegramReceived
        LogNoMatchingDefinitionFromClient(logger, payload.Length, ctx.Id);
        UnknownTelegramReceived?.Invoke(this, new UnknownTelegramEventArgs(payload, byteOrder));
    }

    /// <summary>
    /// Validates the embedded length field (if configured via
    /// <c>.WithLength&lt;TType&gt;(length, byteOffset)</c>).
    /// Returns <c>true</c> when valid (or when no length field is configured).
    /// Returns <c>false</c> and raises <see cref="UnknownTelegramReceived"/> on mismatch.
    /// </summary>
    private bool ValidateLength(TelegramDefinition def, byte[] payload, ClientContext ctx)
    {
        if (def.LengthByteOffset < 0) return true;

        int fieldEnd = def.LengthByteOffset + S7TypeConverter.GetWireSize(def.LengthDataType);
        if (payload.Length < fieldEnd)
        {
            LogLengthFieldBeyondPayloadFromClient(logger, def.Id, ctx.Id, def.LengthByteOffset, payload.Length);
            UnknownTelegramReceived?.Invoke(this, new UnknownTelegramEventArgs(payload, byteOrder));
            return false;
        }

        long receivedLength = TelegramIdFramer.ReadLength(payload, def.LengthByteOffset, def.LengthDataType, byteOrder);
        if (receivedLength != def.ConfiguredWireSize)
        {
            LogLengthFieldMismatchFromClient(logger, def.Id, ctx.Id, def.ConfiguredWireSize, receivedLength);
            UnknownTelegramReceived?.Invoke(this, new UnknownTelegramEventArgs(payload, byteOrder));
            return false;
        }

        return true;
    }

    private void TryDeserializeAndFire(TelegramDefinition def, byte[] payload, ClientContext ctx)
    {
        try
        {
            var telegram = TelegramSerializer.Deserialize(def, payload, byteOrder);
            TelegramReceived?.Invoke(this, new TelegramReceivedEventArgs(telegram, payload, ctx.RemoteAddress, ctx.Port));
        }
        catch (Exception ex)
        {
            LogDeserializeFailedFromClient(logger, ex, def.Id, ctx.Id);
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

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}