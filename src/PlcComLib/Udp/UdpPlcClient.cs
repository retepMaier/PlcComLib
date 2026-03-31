using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PlcComLib.Core;
using PlcComLib.Telegrams;

namespace PlcComLib.Udp;

/// <summary>
/// UDP client for sending telegrams to a remote PLC endpoint. Thread-safe.
/// </summary>
public sealed class UdpPlcClient : IPlcConnection
{
    private readonly ConnectionConfiguration _config;
    private readonly TelegramRegistry _registry;
    private readonly ILogger<UdpPlcClient>? _logger;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;
    private bool _disposed;

    public event EventHandler<TelegramReceivedEventArgs>? TelegramReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public bool IsConnected => _isConnected;

    public UdpPlcClient(
        ConnectionConfiguration config,
        TelegramRegistry registry,
        ILogger<UdpPlcClient>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _udpClient = new UdpClient();
        _udpClient.Client.SendTimeout = _config.TimeoutMs;
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

    public async Task SendAsync(Telegram telegram, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isConnected || _udpClient == null)
            throw new InvalidOperationException("UDP client is not started.");

        var payload = TelegramSerializer.Serialize(telegram);
        var endpoint = new IPEndPoint(IPAddress.Parse(_config.Host), _config.Port);

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
