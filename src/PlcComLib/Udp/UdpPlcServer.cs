using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlcComLib.Core;
using PlcComLib.Core.Events;
using PlcComLib.Core.Internal;
using PlcComLib.DataTypes;
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
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;
    private readonly ConnectionConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));

    private readonly TelegramDispatcher _dispatcher = new(registry, byteOrder, logger);

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
        UdpSocket.DisableConnectionReset(_udpClient.Client);
        if (_config.ReceiveBufferSize > 0) _udpClient.Client.ReceiveBufferSize = _config.ReceiveBufferSize;
        if (_config.SendBufferSize    > 0) _udpClient.Client.SendBufferSize    = _config.SendBufferSize;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isConnected = true;
        EventRaiser.Raise(ConnectionStateChanged, this, new ConnectionStateChangedEventArgs(true, "UDP server started"), _logger);
        LogServerListening(_logger, _config.Host, _config.Port);
        _receiveTask = ReceiveLoopAsync(_udpClient, _cts.Token);
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
        EventRaiser.Raise(ConnectionStateChanged, this, new ConnectionStateChangedEventArgs(false, "UDP server stopped"), _logger);
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
        await SendToEndpointAsync(_dispatcher.Serialize(telegram), _lastSenderEndpoint, cancellationToken);
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
        await SendToEndpointAsync(_dispatcher.Serialize(telegram), endpoint, cancellationToken);
    }

    /// <summary>Sends a strongly-typed telegram to a specific remote address and port.</summary>
    public async Task SendToAsync<T>(string remoteAddress, int port, T telegram, CancellationToken cancellationToken = default)where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null) throw new InvalidOperationException("UDP server is not started.");
        if (!IPAddress.TryParse(remoteAddress, out var ipAddress))
            throw new ArgumentException($"Invalid IP address: '{remoteAddress}'.", nameof(remoteAddress));
        await SendToEndpointAsync(_dispatcher.Serialize(telegram), new IPEndPoint(ipAddress, port), cancellationToken);
    }

    private async Task SendToEndpointAsync(byte[] payload, IPEndPoint endpoint, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await _udpClient!.SendAsync(payload, endpoint, ct);
            EventRaiser.Raise(RawBytesSent, this, new RawBytesEventArgs(payload, endpoint.Address.ToString(), endpoint.Port), _logger);
        }
        finally { _sendLock.Release(); }
    }

    // ── Typed subscription ────────────────────────────────────────────────────

    /// <summary>
    /// Invokes <paramref name="handler"/> whenever a telegram of type <typeparamref name="T"/>
    /// (as registered on this server's builder) arrives. Handlers run independently of
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
    /// (as registered on this server's builder) arrives.
    /// The handler receives the deserialised telegram plus the remote IP address and port.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> that unsubscribes when disposed.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> was not registered on the builder.</exception>
    public IDisposable Subscribe<T>(Action<T, string, int> handler) where T : ITypedS7Telegram<T>
        => _dispatcher.Subscribe(handler);

    // ── Receive loop ──────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(UdpClient udpClient, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await udpClient.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex) when (UdpSocket.IsTransient(ex))
            {
                // e.g. ICMP port unreachable after replying to a peer that went away — keep listening.
                LogUdpTransientReceiveError(_logger, ex.SocketErrorCode);
                continue;
            }
            catch (Exception ex)
            {
                LogUdpReceiveError(_logger, ex);
                _isConnected = false;
                EventRaiser.Raise(ConnectionStateChanged, this,
                    new ConnectionStateChangedEventArgs(false, $"UDP receive failed: {ex.Message}"), _logger);
                break;
            }

            _lastSenderEndpoint = result.RemoteEndPoint;
            string remoteAddress = result.RemoteEndPoint.Address.ToString();
            int    remotePort    = result.RemoteEndPoint.Port;
            EventRaiser.Raise(RawBytesReceived, this, new RawBytesEventArgs(result.Buffer, remoteAddress, remotePort), _logger);
            DispatchTelegram(result.Buffer, remoteAddress, remotePort);
        }
    }

    private void DispatchTelegram(byte[] payload, string remoteAddress, int port)
    {
        var def = _dispatcher.Match(payload, $"{remoteAddress}:{port}");
        if (def is null)
        {
            EventRaiser.Raise(UnknownTelegramReceived, this, new UnknownTelegramEventArgs(payload, byteOrder), _logger);
            return;
        }
        _dispatcher.Deliver(def, payload, remoteAddress, port, this, TelegramReceived);
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
