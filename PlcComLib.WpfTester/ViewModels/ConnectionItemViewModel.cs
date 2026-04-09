using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PlcComLib.Core.PlcTypes;
using PlcComLib.Core.Events;
using PlcComLib.DataTypes;
using PlcComLib.Tcp;
using PlcComLib.Telegrams;
using PlcComLib.Udp;
using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.Services;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class ConnectionItemViewModel(ConnectionSettings settings, ILogService logService, ILogger logger) : ObservableObject, IAsyncDisposable
{
    private IAsyncDisposable? _connection;

    // Abstracted event accessors stored at build time
    private Action? _subscribeEvents;
    private Action? _unsubscribeEvents;
    private Func<Task>? _startAction;
    private Func<Task>? _stopAction;
    private Func<Telegram, Task>? _sendAction;

    [ObservableProperty] public partial bool IsConnected { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = "Stopped";

    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string SendHex { get; set; } = string.Empty;

    [ObservableProperty] public partial ConnectionTelegramEntry? SelectedTelegram { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsEditing { get; set; }

    public bool CanEdit => !IsConnected && !IsBusy;

    public IReadOnlyList<ByteOrder> ByteOrders { get; } = Enum.GetValues<ByteOrder>();
    public ConnectionSettings Settings { get; } = settings;
    public string DisplayName => string.IsNullOrWhiteSpace(Settings.Name)
        ? $"{Settings.Type} {Settings.Host}:{Settings.Port}"
        : Settings.Name;
    public string TypeLabel => Settings.Type.ToString();
    public string HostPort => $"{Settings.Host}:{Settings.Port}";

    public ObservableCollection<MessageEntry> Messages { get; } = [];

    [RelayCommand]
    public async Task StartAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            BuildConnection();
            if (_startAction is not null)
            {
                await _startAction();
                StatusText = Settings.Type is ConnectionType.TcpServer or ConnectionType.UdpServer
                    ? "Listening"
                    : "Connecting...";
            }
        }
        catch (Exception ex)
        {
            logService.Log(Models.LogLevel.Error, DisplayName, $"Start failed: {ex.Message}");
            StatusText = "Error";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StopAsync()
    {
        if (_stopAction is not null)
        {
            try { await _stopAction(); }
            catch (Exception ex)
            {
                logService.Log(Models.LogLevel.Warning, DisplayName, $"Stop error: {ex.Message}");
            }
        }
        _unsubscribeEvents?.Invoke();
        if (_connection is not null)
        {
            try { await _connection.DisposeAsync(); }
            catch { /* ignore */ }
            _connection = null;
        }
        IsConnected = false;
        StatusText = "Stopped";
    }

    [RelayCommand]
    public async Task StopConnectionAsync() => await StopAsync();

    [RelayCommand]
    public async Task SendTelegramAsync()
    {
        if (_sendAction is null || SelectedTelegram is null) return;
        try
        {
            var def = SelectedTelegram.ToTelegramDefinition();
            var telegram = new Telegram(def);
            foreach (var field in SelectedTelegram.Telegram.Fields)
            {
                if (string.IsNullOrEmpty(field.DefaultValue)) continue;
                var value = ParseDefaultValue(field.DefaultValue, field.DataType);
                if (value is not null)
                    telegram.SetValue(field.Name, value);
            }
            await _sendAction(telegram);
        }
        catch (Exception ex)
        {
            logService.Log(Models.LogLevel.Error, DisplayName, $"Send failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ToggleEdit()
    {
        if (!CanEdit && !IsEditing) return;
        IsEditing = !IsEditing;
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        if (value) IsEditing = false;
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
    }

    private static object? ParseDefaultValue(string text, S7DataType dataType)
    {
        try
        {
            return dataType switch
            {
                S7DataType.Bool => text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase),
                S7DataType.Byte or S7DataType.USInt => byte.Parse(text),
                S7DataType.Word or S7DataType.UInt => ushort.Parse(text),
                S7DataType.DWord or S7DataType.UDInt => uint.Parse(text),
                S7DataType.LWord or S7DataType.ULInt => ulong.Parse(text),
                S7DataType.SInt => sbyte.Parse(text),
                S7DataType.Int => short.Parse(text),
                S7DataType.DInt => int.Parse(text),
                S7DataType.LInt => long.Parse(text),
                S7DataType.Real => float.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
                S7DataType.LReal => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
                S7DataType.Char => string.IsNullOrEmpty(text) ? null : (object)text[0],
                S7DataType.WChar => string.IsNullOrEmpty(text) ? null : (object)text[0],
                S7DataType.S7String or S7DataType.S7WString or S7DataType.CharArray => text,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    public async Task SendRawHexAsync()
    {
        if (string.IsNullOrWhiteSpace(SendHex)) return;
        try
        {
            var bytes = ParseHex(SendHex);
            if (bytes is null || bytes.Length == 0) return;

            // Create a raw telegram with a dummy definition
            var def = new TelegramDefinition
            {
                Id = "raw",
                Name = "Raw",
                Fields = { new TelegramField { Name = "data", DataType = S7DataType.Raw, RawByteCount = bytes.Length } }
            };
            var telegram = new Telegram(def);
            telegram.SetValue("data", bytes);
            if (_sendAction is not null)
                await _sendAction(telegram);
        }
        catch (Exception ex)
        {
            logService.Log(Models.LogLevel.Error, DisplayName, $"Send raw failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ClearMessages() => Messages.Clear();

    private void BuildConnection()
    {
        _unsubscribeEvents?.Invoke();
        if (_connection is not null)
        {
            _ = _connection.DisposeAsync();
            _connection = null;
        }

        switch (Settings.Type)
        {
            case ConnectionType.TcpClient:
                BuildTcpClient();
                break;
            case ConnectionType.TcpServer:
                BuildTcpServer();
                break;
            case ConnectionType.UdpClient:
                BuildUdpClient();
                break;
            case ConnectionType.UdpServer:
                BuildUdpServer();
                break;
        }
    }

    private void BuildTcpClient()
    {
        var builder = new TcpPlcClientBuilder()
            .ConnectTo(Settings.Host, Settings.Port)
            .WithByteOrder(Settings.ByteOrder)
            .WithLogger(logger)
            .WithTimeout(TimeSpan.FromSeconds(Settings.TimeoutSeconds))
            .WithReconnectInterval(TimeSpan.FromSeconds(Settings.ReconnectIntervalSeconds))            
            .WithNoDelay(Settings.NoDelay);

        
       




        if (Settings.ReceiveBufferSize > 0) builder.WithReceiveBufferSize(Settings.ReceiveBufferSize);
        if (Settings.SendBufferSize > 0) builder.WithSendBufferSize(Settings.SendBufferSize);

        foreach (var t in Settings.Telegrams)
            RegisterTelegram(builder, t);

        var client = builder.Build();
        _connection = client;
        _startAction = () => client.StartAsync();
        _stopAction = () => client.StopAsync();
        _sendAction = t => client.SendAsync(t);

        client.ConnectionStateChanged += OnConnectionStateChanged;
        client.TelegramReceived += OnTelegramReceived;
        client.RawBytesReceived += OnRawBytesReceived;
        client.RawBytesSent += OnRawBytesSent;
        client.UnknownTelegramReceived += OnUnknownTelegramReceived;

        _unsubscribeEvents = () =>
        {
            client.ConnectionStateChanged -= OnConnectionStateChanged;
            client.TelegramReceived -= OnTelegramReceived;
            client.RawBytesReceived -= OnRawBytesReceived;
            client.RawBytesSent -= OnRawBytesSent;
            client.UnknownTelegramReceived -= OnUnknownTelegramReceived;
        };
    }

    private void BuildTcpServer()
    {
        var builder = new TcpPlcServerBuilder()
            .ListenOn(Settings.Host, Settings.Port)
            .WithByteOrder(Settings.ByteOrder)
            .WithTimeout(TimeSpan.FromSeconds(Settings.TimeoutSeconds))
            .WithMaxConnections(Settings.MaxConnections)
            .WithNoDelay(Settings.NoDelay);

        if (Settings.ReceiveBufferSize > 0) builder.WithReceiveBufferSize(Settings.ReceiveBufferSize);
        if (Settings.SendBufferSize > 0) builder.WithSendBufferSize(Settings.SendBufferSize);

        foreach (var t in Settings.Telegrams)
            RegisterTelegram(builder, t);

        var server = builder.Build();
        _connection = server;
        _startAction = () => server.StartAsync();
        _stopAction = () => server.StopAsync();
        _sendAction = t => server.SendAsync(t);

        server.ConnectionStateChanged += OnConnectionStateChanged;
        server.TelegramReceived += OnTelegramReceived;
        server.RawBytesReceived += OnRawBytesReceived;
        server.RawBytesSent += OnRawBytesSent;
        server.UnknownTelegramReceived += OnUnknownTelegramReceived;

        _unsubscribeEvents = () =>
        {
            server.ConnectionStateChanged -= OnConnectionStateChanged;
            server.TelegramReceived -= OnTelegramReceived;
            server.RawBytesReceived -= OnRawBytesReceived;
            server.RawBytesSent -= OnRawBytesSent;
            server.UnknownTelegramReceived -= OnUnknownTelegramReceived;
        };
    }

    private void BuildUdpClient()
    {
        var builder = new UdpPlcClientBuilder()
            .SendTo(Settings.Host, Settings.Port)
            .WithByteOrder(Settings.ByteOrder)
            .WithTimeout(TimeSpan.FromSeconds(Settings.TimeoutSeconds));

        if (Settings.ReceiveBufferSize > 0) builder.WithReceiveBufferSize(Settings.ReceiveBufferSize);
        if (Settings.SendBufferSize > 0) builder.WithSendBufferSize(Settings.SendBufferSize);

        foreach (var t in Settings.Telegrams)
            RegisterTelegram(builder, t);

        var client = builder.Build();
        _connection = client;
        _startAction = () => client.StartAsync();
        _stopAction = () => client.StopAsync();
        _sendAction = t => client.SendAsync(t);

        client.ConnectionStateChanged += OnConnectionStateChanged;
        client.TelegramReceived += OnTelegramReceived;
        client.RawBytesReceived += OnRawBytesReceived;
        client.RawBytesSent += OnRawBytesSent;
        client.UnknownTelegramReceived += OnUnknownTelegramReceived;

        _unsubscribeEvents = () =>
        {
            client.ConnectionStateChanged -= OnConnectionStateChanged;
            client.TelegramReceived -= OnTelegramReceived;
            client.RawBytesReceived -= OnRawBytesReceived;
            client.RawBytesSent -= OnRawBytesSent;
            client.UnknownTelegramReceived -= OnUnknownTelegramReceived;
        };
    }

    private void BuildUdpServer()
    {
        var builder = new UdpPlcServerBuilder()
            .ListenOn(Settings.Host, Settings.Port)
            .WithByteOrder(Settings.ByteOrder);

        if (Settings.ReceiveBufferSize > 0) builder.WithReceiveBufferSize(Settings.ReceiveBufferSize);
        if (Settings.SendBufferSize > 0) builder.WithSendBufferSize(Settings.SendBufferSize);

        foreach (var t in Settings.Telegrams)
            RegisterTelegram(builder, t);

        var server = builder.Build();
        _connection = server;
        _startAction = () => server.StartAsync();
        _stopAction = () => server.StopAsync();
        _sendAction = t => server.SendAsync(t);

        server.ConnectionStateChanged += OnConnectionStateChanged;
        server.TelegramReceived += OnTelegramReceived;
        server.RawBytesReceived += OnRawBytesReceived;
        server.RawBytesSent += OnRawBytesSent;
        server.UnknownTelegramReceived += OnUnknownTelegramReceived;

        _unsubscribeEvents = () =>
        {
            server.ConnectionStateChanged -= OnConnectionStateChanged;
            server.TelegramReceived -= OnTelegramReceived;
            server.RawBytesReceived -= OnRawBytesReceived;
            server.RawBytesSent -= OnRawBytesSent;
            server.UnknownTelegramReceived -= OnUnknownTelegramReceived;
        };
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsConnected = e.IsConnected;
            StatusText = e.IsConnected
                ? (Settings.Type is ConnectionType.TcpServer or ConnectionType.UdpServer ? "Listening/Connected" : "Connected")
                : "Disconnected";
            logService.Log(
                e.IsConnected ? Models.LogLevel.Info : Models.LogLevel.Warning,
                DisplayName,
                e.IsConnected ? "Connected" : $"Disconnected: {e.Reason}");
        });
    }

    private void OnTelegramReceived(object? sender, TelegramReceivedEventArgs e)
    {
        var fields = new Dictionary<string, string>();
        foreach (var name in e.Telegram.FieldNames)
        {
            try { fields[name] = e.Telegram.GetValue(name)?.ToString() ?? "(null)"; }
            catch { fields[name] = "(error)"; }
        }

        var entry = new MessageEntry
        {
            Direction = MessageDirection.Received,
            RawBytes = e.RawPayload,
            Fields = fields,
            TelegramName = e.Telegram.Definition.Name,
            RemoteAddress = e.RemoteAddress,
            RemotePort = e.Port,
        };
        AddMessage(entry);
    }

    private void OnRawBytesReceived(object? sender, RawBytesEventArgs e)
    {
        // Only add if no telegram was matched (avoid duplicates with OnTelegramReceived)
    }

    private void OnRawBytesSent(object? sender, RawBytesEventArgs e)
    {
        var entry = new MessageEntry
        {
            Direction = MessageDirection.Sent,
            RawBytes = e.Data,
            RemoteAddress = e.RemoteAddress,
            RemotePort = e.Port,
        };
        AddMessage(entry);
    }

    private void OnUnknownTelegramReceived(object? sender, UnknownTelegramEventArgs e)
    {
        var entry = new MessageEntry
        {
            Direction = MessageDirection.Received,
            RawBytes = e.Payload,
            TelegramName = "(unknown)",
        };
        AddMessage(entry);
        logService.Log(Models.LogLevel.Warning, DisplayName, $"Unknown telegram received: {entry.HexDisplay}");
    }

    private void AddMessage(MessageEntry entry)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Messages.Add(entry);
            while (Messages.Count > 50)
                Messages.RemoveAt(0);
        });
    }

    private void RegisterTelegram(TcpPlcClientBuilder builder, ConnectionTelegramEntry entry)
    {
        var def = entry.ToTelegramDefinition();
        builder.RegisterTelegram(def);
        ApplyFraming(def,
            (value, offset) => builder.WithMessageId<S7Byte>(value, offset),
            (value, offset) => builder.WithMessageId<S7SInt>(value, offset),
            (value, offset) => builder.WithMessageId<S7Word>(value, offset),
            (value, offset) => builder.WithMessageId<S7Int>(value, offset),
            (value, offset) => builder.WithMessageId<S7DWord>(value, offset),
            (value, offset) => builder.WithMessageId<S7DInt>(value, offset),
            (value, offset) => builder.WithMessageId<S7LWord>(value, offset),
            (value, offset) => builder.WithMessageId<S7LInt>(value, offset),
            (value, offset) => builder.WithLength<S7Byte>(value, offset),
            (value, offset) => builder.WithLength<S7SInt>(value, offset),
            (value, offset) => builder.WithLength<S7Word>(value, offset),
            (value, offset) => builder.WithLength<S7Int>(value, offset),
            (value, offset) => builder.WithLength<S7DWord>(value, offset),
            (value, offset) => builder.WithLength<S7DInt>(value, offset),
            (value, offset) => builder.WithLength<S7LWord>(value, offset),
            (value, offset) => builder.WithLength<S7LInt>(value, offset));
        LogTelegramRegistration(def);
    }

    private void RegisterTelegram(TcpPlcServerBuilder builder, ConnectionTelegramEntry entry)
    {
        var def = entry.ToTelegramDefinition();
        builder.RegisterTelegram(def);
        ApplyFraming(def,
            (value, offset) => builder.WithMessageId<S7Byte>(value, offset),
            (value, offset) => builder.WithMessageId<S7SInt>(value, offset),
            (value, offset) => builder.WithMessageId<S7Word>(value, offset),
            (value, offset) => builder.WithMessageId<S7Int>(value, offset),
            (value, offset) => builder.WithMessageId<S7DWord>(value, offset),
            (value, offset) => builder.WithMessageId<S7DInt>(value, offset),
            (value, offset) => builder.WithMessageId<S7LWord>(value, offset),
            (value, offset) => builder.WithMessageId<S7LInt>(value, offset),
            (value, offset) => builder.WithLength<S7Byte>(value, offset),
            (value, offset) => builder.WithLength<S7SInt>(value, offset),
            (value, offset) => builder.WithLength<S7Word>(value, offset),
            (value, offset) => builder.WithLength<S7Int>(value, offset),
            (value, offset) => builder.WithLength<S7DWord>(value, offset),
            (value, offset) => builder.WithLength<S7DInt>(value, offset),
            (value, offset) => builder.WithLength<S7LWord>(value, offset),
            (value, offset) => builder.WithLength<S7LInt>(value, offset));
        LogTelegramRegistration(def);
    }

    private void RegisterTelegram(UdpPlcClientBuilder builder, ConnectionTelegramEntry entry)
    {
        var def = entry.ToTelegramDefinition();
        builder.RegisterTelegram(def);
        ApplyFraming(def,
            (value, offset) => builder.WithMessageId<S7Byte>(value, offset),
            (value, offset) => builder.WithMessageId<S7SInt>(value, offset),
            (value, offset) => builder.WithMessageId<S7Word>(value, offset),
            (value, offset) => builder.WithMessageId<S7Int>(value, offset),
            (value, offset) => builder.WithMessageId<S7DWord>(value, offset),
            (value, offset) => builder.WithMessageId<S7DInt>(value, offset),
            (value, offset) => builder.WithMessageId<S7LWord>(value, offset),
            (value, offset) => builder.WithMessageId<S7LInt>(value, offset),
            (value, offset) => builder.WithLength<S7Byte>(value, offset),
            (value, offset) => builder.WithLength<S7SInt>(value, offset),
            (value, offset) => builder.WithLength<S7Word>(value, offset),
            (value, offset) => builder.WithLength<S7Int>(value, offset),
            (value, offset) => builder.WithLength<S7DWord>(value, offset),
            (value, offset) => builder.WithLength<S7DInt>(value, offset),
            (value, offset) => builder.WithLength<S7LWord>(value, offset),
            (value, offset) => builder.WithLength<S7LInt>(value, offset));
        LogTelegramRegistration(def);
    }

    private void RegisterTelegram(UdpPlcServerBuilder builder, ConnectionTelegramEntry entry)
    {
        var def = entry.ToTelegramDefinition();
        builder.RegisterTelegram(def);
        ApplyFraming(def,
            (value, offset) => builder.WithMessageId<S7Byte>(value, offset),
            (value, offset) => builder.WithMessageId<S7SInt>(value, offset),
            (value, offset) => builder.WithMessageId<S7Word>(value, offset),
            (value, offset) => builder.WithMessageId<S7Int>(value, offset),
            (value, offset) => builder.WithMessageId<S7DWord>(value, offset),
            (value, offset) => builder.WithMessageId<S7DInt>(value, offset),
            (value, offset) => builder.WithMessageId<S7LWord>(value, offset),
            (value, offset) => builder.WithMessageId<S7LInt>(value, offset),
            (value, offset) => builder.WithLength<S7Byte>(value, offset),
            (value, offset) => builder.WithLength<S7SInt>(value, offset),
            (value, offset) => builder.WithLength<S7Word>(value, offset),
            (value, offset) => builder.WithLength<S7Int>(value, offset),
            (value, offset) => builder.WithLength<S7DWord>(value, offset),
            (value, offset) => builder.WithLength<S7DInt>(value, offset),
            (value, offset) => builder.WithLength<S7LWord>(value, offset),
            (value, offset) => builder.WithLength<S7LInt>(value, offset));
        LogTelegramRegistration(def);
    }

    private void ApplyFraming(
        TelegramDefinition def,
        Action<long, int> useByteMessageId,
        Action<long, int> useSIntMessageId,
        Action<long, int> useWordMessageId,
        Action<long, int> useIntMessageId,
        Action<long, int> useDWordMessageId,
        Action<long, int> useDIntMessageId,
        Action<long, int> useLWordMessageId,
        Action<long, int> useLIntMessageId,
        Action<long, int> useByteLength,
        Action<long, int> useSIntLength,
        Action<long, int> useWordLength,
        Action<long, int> useIntLength,
        Action<long, int> useDWordLength,
        Action<long, int> useDIntLength,
        Action<long, int> useLWordLength,
        Action<long, int> useLIntLength)
    {
        if (def.MessageId != 0)
        {
            if (!ApplyNumericFraming(def.MessageIdDataType, def.MessageId, def.MessageIdByteOffset,
                useByteMessageId, useSIntMessageId, useWordMessageId, useIntMessageId,
                useDWordMessageId, useDIntMessageId, useLWordMessageId, useLIntMessageId))
            {
                logService.Log(Models.LogLevel.Warning, DisplayName,
                    $"Telegram '{def.Name}' uses unsupported message ID type '{def.MessageIdDataType}'.");
            }
        }

        if (def.LengthByteOffset >= 0 && def.ConfiguredWireSize > 0)
        {
            if (!ApplyNumericFraming(def.LengthDataType, def.ConfiguredWireSize, def.LengthByteOffset,
                useByteLength, useSIntLength, useWordLength, useIntLength,
                useDWordLength, useDIntLength, useLWordLength, useLIntLength))
            {
                logService.Log(Models.LogLevel.Warning, DisplayName,
                    $"Telegram '{def.Name}' uses unsupported length type '{def.LengthDataType}'.");
            }
        }
    }

    private static bool ApplyNumericFraming(
        S7DataType dataType,
        long value,
        int offset,
        Action<long, int> useByte,
        Action<long, int> useSInt,
        Action<long, int> useWord,
        Action<long, int> useInt,
        Action<long, int> useDWord,
        Action<long, int> useDInt,
        Action<long, int> useLWord,
        Action<long, int> useLInt)
    {
        switch (dataType)
        {
            case S7DataType.Byte:
            case S7DataType.USInt:
                useByte(value, offset);
                return true;
            case S7DataType.SInt:
                useSInt(value, offset);
                return true;
            case S7DataType.Word:
            case S7DataType.UInt:
                useWord(value, offset);
                return true;
            case S7DataType.Int:
                useInt(value, offset);
                return true;
            case S7DataType.DWord:
            case S7DataType.UDInt:
                useDWord(value, offset);
                return true;
            case S7DataType.DInt:
                useDInt(value, offset);
                return true;
            case S7DataType.LWord:
            case S7DataType.ULInt:
                useLWord(value, offset);
                return true;
            case S7DataType.LInt:
                useLInt(value, offset);
                return true;
            default:
                return false;
        }
    }

    private void LogTelegramRegistration(TelegramDefinition def)
    {
        logService.Log(
            Models.LogLevel.Info,
            DisplayName,
            $"Telegram '{def.Name}' registered with id={def.MessageId} ({def.MessageIdDataType} @ {def.MessageIdByteOffset}), length={def.ConfiguredWireSize} ({def.LengthDataType} @ {def.LengthByteOffset})");
    }

    private static byte[]? ParseHex(string hex)
    {
        hex = hex.Replace(" ", "").Replace("-", "");
        if (hex.Length % 2 != 0) return null;
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                return null;
        }
        return bytes;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
