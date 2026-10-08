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
/// UDP client for sending telegrams to a remote endpoint and receiving datagrams. Thread-safe.
/// </summary>
/// <remarks>
/// The socket is bound to <see cref="ConnectionConfiguration.LocalPort"/> (or a free port chosen by the OS)
/// when the client starts, so replies can be received before anything has been sent.
/// </remarks>
public sealed partial class UdpPlcClient(
    ConnectionConfiguration config,
    TelegramRegistry registry,
    ILogger<UdpPlcClient>? logger = null,
    ByteOrder byteOrder = ByteOrder.BigEndian) : IPlcConnection
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;
    private readonly ConnectionConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly TelegramDispatcher _dispatcher = new(registry, byteOrder, logger);

    private UdpClient? _udpClient;
    private IPEndPoint? _remoteEndpoint;
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

    /// <summary>The local endpoint the client is bound to, or <c>null</c> when not started.</summary>
    public IPEndPoint? LocalEndpoint => _udpClient?.Client.LocalEndPoint as IPEndPoint;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var remote = await ResolveRemoteAsync(cancellationToken);
        // Bind before receiving: an unbound socket cannot receive, and the receive loop would die at once.
        var udpClient = new UdpClient(remote.AddressFamily);
        try
        {
            UdpSocket.DisableConnectionReset(udpClient.Client);
            udpClient.Client.SendTimeout    = _config.TimeoutMs;
            udpClient.Client.ReceiveTimeout = _config.TimeoutMs;
            if (_config.ReceiveBufferSize > 0) udpClient.Client.ReceiveBufferSize = _config.ReceiveBufferSize;
            if (_config.SendBufferSize    > 0) udpClient.Client.SendBufferSize    = _config.SendBufferSize;
            var any = remote.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
            udpClient.Client.Bind(new IPEndPoint(any, _config.LocalPort));
        }
        catch
        {
            udpClient.Dispose();
            throw;
        }

        _udpClient      = udpClient;
        _remoteEndpoint = remote;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isConnected = true;
        LogUdpClientBound(_logger, LocalEndpoint, remote);
        EventRaiser.Raise(ConnectionStateChanged, this, new ConnectionStateChangedEventArgs(true, "UDP client started"), _logger);
        _receiveTask = ReceiveLoopAsync(udpClient, _cts.Token);
    }

    private async Task<IPEndPoint> ResolveRemoteAsync(CancellationToken ct)
    {
        if (IPAddress.TryParse(_config.Host, out var address))
            return new IPEndPoint(address, _config.Port);

        var addresses = await Dns.GetHostAddressesAsync(_config.Host, ct);
        var resolved = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                       ?? addresses.FirstOrDefault()
                       ?? throw new SocketException((int)SocketError.HostNotFound);
        return new IPEndPoint(resolved, _config.Port);
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
        EventRaiser.Raise(ConnectionStateChanged, this, new ConnectionStateChangedEventArgs(false, "UDP client stopped"), _logger);
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    /// <summary>Serialises and sends a legacy untyped telegram.</summary>
    public async Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient is null)
            throw new InvalidOperationException("UDP client is not started.");
        await SendPayloadAsync(TelegramSerializer.Serialize(telegram, byteOrder), cancellationToken);
    }

    /// <summary>
    /// Serialises and sends a strongly-typed telegram. The MessageId and length configured on the
    /// builder are written into their header fields.
    /// </summary>
    public async Task SendAsync<T>(T telegram, CancellationToken cancellationToken = default) where T : ITypedS7Telegram<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient is null)
            throw new InvalidOperationException("UDP client is not started.");
        await SendPayloadAsync(_dispatcher.Serialize(telegram), cancellationToken);
    }

    private async Task SendPayloadAsync(byte[] payload, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            var udpClient = _udpClient ?? throw new InvalidOperationException("UDP client is not started.");
            await udpClient.SendAsync(payload, _remoteEndpoint!, ct);
            EventRaiser.Raise(RawBytesSent, this, new RawBytesEventArgs(payload, _config.Host, _config.Port), _logger);
        }
        finally { _sendLock.Release(); }
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
