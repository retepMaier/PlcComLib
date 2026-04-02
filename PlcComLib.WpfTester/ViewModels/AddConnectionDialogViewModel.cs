using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcComLib.DataTypes;
using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.Services;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class AddConnectionDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private ConnectionType _selectedType = ConnectionType.TcpClient;

    [ObservableProperty]
    private string _host = "127.0.0.1";

    [ObservableProperty]
    private int _port = 2000;

    [ObservableProperty]
    private ByteOrder _selectedByteOrder = ByteOrder.BigEndian;

    [ObservableProperty]
    private int _timeoutSeconds = 10;

    [ObservableProperty]
    private int _reconnectIntervalSeconds = 5;

    [ObservableProperty]
    private int _maxConnections = 10;

    [ObservableProperty]
    private bool _noDelay = false;

    [ObservableProperty]
    private int _receiveBufferSize = 0;

    [ObservableProperty]
    private int _sendBufferSize = 0;

    [ObservableProperty]
    private bool _isTcpClient = true;

    [ObservableProperty]
    private bool _isTcpServer = false;

    [ObservableProperty]
    private bool _isUdp = false;

    public ObservableCollection<TelegramDefinitionModel> Telegrams { get; } = [];
    public ObservableCollection<TelegramFieldDefinition> CurrentTelegramFields { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTelegram))]
    private TelegramDefinitionModel? _selectedTelegram;

    public bool HasSelectedTelegram => SelectedTelegram is not null;

    [ObservableProperty]
    private string _newTelegramName = string.Empty;

    [ObservableProperty]
    private long _newTelegramMessageId = 0;

    [ObservableProperty]
    private int _newTelegramMessageIdOffset = 0;

    [ObservableProperty]
    private S7DataType _newTelegramMessageIdType = S7DataType.Word;

    [ObservableProperty]
    private string _newFieldName = string.Empty;

    [ObservableProperty]
    private S7DataType _newFieldDataType = S7DataType.Word;

    [ObservableProperty]
    private byte _newFieldMaxStringLength = 0;

    [ObservableProperty]
    private int _newFieldRawByteCount = 0;

    public IReadOnlyList<ConnectionType> ConnectionTypes { get; } = Enum.GetValues<ConnectionType>();
    public IReadOnlyList<ByteOrder> ByteOrders { get; } = Enum.GetValues<ByteOrder>();
    public IReadOnlyList<S7DataType> DataTypes { get; } = Enum.GetValues<S7DataType>();

    partial void OnSelectedTypeChanged(ConnectionType value)
    {
        IsTcpClient = value == ConnectionType.TcpClient;
        IsTcpServer = value == ConnectionType.TcpServer;
        IsUdp = value is ConnectionType.UdpClient or ConnectionType.UdpServer;
    }

    [RelayCommand]
    private void AddTelegram()
    {
        if (string.IsNullOrWhiteSpace(NewTelegramName)) return;
        var t = new TelegramDefinitionModel
        {
            Name = NewTelegramName,
            MessageId = NewTelegramMessageId,
            MessageIdByteOffset = NewTelegramMessageIdOffset,
            MessageIdDataType = NewTelegramMessageIdType,
        };
        Telegrams.Add(t);
        SelectedTelegram = t;
        CurrentTelegramFields.Clear();
        NewTelegramName = string.Empty;
        NewTelegramMessageId = 0;
    }

    [RelayCommand]
    private void RemoveTelegram(TelegramDefinitionModel? t)
    {
        if (t is null) return;
        Telegrams.Remove(t);
        if (SelectedTelegram == t)
        {
            SelectedTelegram = null;
            CurrentTelegramFields.Clear();
        }
    }

    [RelayCommand]
    private void SelectTelegram(TelegramDefinitionModel? t)
    {
        if (t is null) return;
        SelectedTelegram = t;
        CurrentTelegramFields.Clear();
        foreach (var f in t.Fields)
            CurrentTelegramFields.Add(f);
    }

    [RelayCommand]
    private void AddField()
    {
        if (SelectedTelegram is null || string.IsNullOrWhiteSpace(NewFieldName)) return;
        var field = new TelegramFieldDefinition
        {
            Name = NewFieldName,
            DataType = NewFieldDataType,
            MaxStringLength = NewFieldMaxStringLength,
            RawByteCount = NewFieldRawByteCount,
        };
        SelectedTelegram.Fields.Add(field);
        CurrentTelegramFields.Add(field);
        NewFieldName = string.Empty;
    }

    [RelayCommand]
    private void RemoveField(TelegramFieldDefinition? f)
    {
        if (f is null || SelectedTelegram is null) return;
        SelectedTelegram.Fields.Remove(f);
        CurrentTelegramFields.Remove(f);
    }

    public ConnectionSettings BuildSettings()
    {
        return new ConnectionSettings
        {
            Name = Name,
            Type = SelectedType,
            Host = Host,
            Port = Port,
            ByteOrder = SelectedByteOrder,
            TimeoutSeconds = TimeoutSeconds,
            ReconnectIntervalSeconds = ReconnectIntervalSeconds,
            MaxConnections = MaxConnections,
            NoDelay = NoDelay,
            ReceiveBufferSize = ReceiveBufferSize,
            SendBufferSize = SendBufferSize,
            Telegrams = Telegrams.ToList(),
        };
    }
}
