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

    public ObservableCollection<SelectableTelegramViewModel> AvailableTelegrams { get; } = [];

    public bool HasAvailableTelegrams => AvailableTelegrams.Count > 0;

    public IReadOnlyList<ConnectionType> ConnectionTypes { get; } = Enum.GetValues<ConnectionType>();
    public IReadOnlyList<ByteOrder> ByteOrders { get; } = Enum.GetValues<ByteOrder>();

    public AddConnectionDialogViewModel(ITelegramLibraryService telegramLibrary)
    {
        foreach (var t in telegramLibrary.Telegrams)
            AvailableTelegrams.Add(new SelectableTelegramViewModel(t));
    }

    partial void OnSelectedTypeChanged(ConnectionType value)
    {
        IsTcpClient = value == ConnectionType.TcpClient;
        IsTcpServer = value == ConnectionType.TcpServer;
        IsUdp = value is ConnectionType.UdpClient or ConnectionType.UdpServer;
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
            Telegrams = AvailableTelegrams
                .Where(t => t.IsSelected)
                .Select(t => t.Telegram.DeepCopy())
                .ToList(),
        };
    }
}

