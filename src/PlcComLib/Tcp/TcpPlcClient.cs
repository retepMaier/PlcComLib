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
/// TCP client that connects to a PLC (or any compatible endpoint).
/// Supports automatic reconnection, message framing, typed telegrams, and full-duplex communication.
/// This class is thread-safe.
/// </summary>
public sealed partial class TcpPlcClient(
    ConnectionConfiguration config,
    TelegramRegistry registry,
    ILogger? logger = null,
    ByteOrder byteOrder = ByteOrder.BigEndian) : IPlcConnection
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;
    private readonly ConnectionConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly TelegramIdFramer _framer = new(registry.Definitions, byteOrder);
    private readonly TelegramDispatcher _dispatcher = new(registry, byteOrder, logger);
    private readonly string _source = $"{config.Host}:{config.Port}";

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

        await SendFramedAsync(_dispatcher.Serialize(telegram), cancellationToken);
    }

    private async Task SendFramedAsync(byte[] payload, CancellationToken ct)
    {
        var framed = _framer.Frame(payload);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _stream!.WriteAsync(framed, ct);
            await _stream!.FlushAsync(ct);
            EventRaiser.Raise(RawBytesSent, this, new RawBytesEventArgs(framed, _config.Host, _config.Port), _logger);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ── Typed subscription ────────────────────────────────────────────────────

    /// <summary>
    /// Invokes <paramref name="handler"/> whenever a telegram of type <typeparamref name="T"/>
    /// (as registered on this connection's builder) arrives. Handlers run independently of
    /// <see cref="TelegramReceived"/>; an exception in one is logged and does not affect the other.
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
    /// (as registered on this connection's builder) arrives.
    /// The handler receives the deserialised telegram plus the remote IP address and port.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> that unsubscribes when disposed.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> was not registered on the builder.</exception>
    public IDisposable Subscribe<T>(Action<T, string, int> handler) where T : ITypedS7Telegram<T>
        => _dispatcher.Subscribe(handler);

    // ── Connection loop ───────────────────────────────────────────────────────

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                LogConnecting(_logger, _config.Host, _config.Port);
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
                LogConnectionFailed(_logger, ex, _config.Host, _config.Port, _config.ReconnectIntervalMs);
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
        var accumulator = new FrameAccumulator(_framer, _framer.MaxFrameSize + buffer.Length);

        while (!ct.IsCancellationRequested && _stream != null)
        {
            int bytesRead;
            try
            {
                bytesRead = await _stream.ReadAsync(buffer, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTcpReadError(_logger, ex);
                break;
            }

            if (bytesRead == 0)
            {
                LogRemoteEndpointClosed(_logger);
                break;
            }

            // Only materialise a separate copy for the diagnostic event when someone is listening.
            if (RawBytesReceived is { } rawReceived)
                EventRaiser.Raise(rawReceived, this, new RawBytesEventArgs(buffer.AsSpan(0, bytesRead).ToArray(), _config.Host, _config.Port), _logger);

            accumulator.Append(buffer.AsSpan(0, bytesRead));
            int discarded = accumulator.Drain(DispatchTelegram);
            if (discarded > 0) LogUnrecognisedTelegramId(_logger, discarded);
        }
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    private void DispatchTelegram(byte[] payload)
    {
        var def = _dispatcher.Match(payload, _source);
        if (def is null)
        {
            EventRaiser.Raise(UnknownTelegramReceived, this, new UnknownTelegramEventArgs(payload, byteOrder), _logger);
            return;
        }
        _dispatcher.Deliver(def, payload, _config.Host, _config.Port, this, TelegramReceived);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetConnected(bool connected, string reason)
    {
        _isConnected = connected;
        EventRaiser.Raise(ConnectionStateChanged, this, new ConnectionStateChangedEventArgs(connected, reason), _logger);
        LogConnectionState(_logger, connected ? "Connected" : "Disconnected", reason);
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
}
