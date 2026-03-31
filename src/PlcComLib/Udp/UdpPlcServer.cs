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
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;
    }

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

    public async Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null)
            throw new InvalidOperationException("UDP server is not started.");
        if (_lastSenderEndpoint == null)
            throw new InvalidOperationException("No sender endpoint available yet.");

        var payload = TelegramSerializer.Serialize(telegram);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _udpClient.SendAsync(payload, _lastSenderEndpoint, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendToAsync(IPEndPoint endpoint, Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null)
            throw new InvalidOperationException("UDP server is not started.");

        var payload = TelegramSerializer.Serialize(telegram);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _udpClient.SendAsync(payload, endpoint, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

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
        foreach (var def in _registry.Definitions)
        {
            if (def.TotalWireSize == payload.Length)
            {
                try
                {
                    var telegram = TelegramSerializer.Deserialize(def, payload);
                    TelegramReceived?.Invoke(this, new TelegramReceivedEventArgs(telegram));
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to deserialize telegram '{Id}'.", def.Id);
                }
            }
        }
        _logger?.LogWarning("No matching UDP telegram definition for payload of {Length} bytes.", payload.Length);
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
