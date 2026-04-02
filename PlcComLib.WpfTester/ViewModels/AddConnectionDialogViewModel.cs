using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcComLib.DataTypes;
using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.Services;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class AddConnectionDialogViewModel : ObservableObject
{
    [ObservableProperty]public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]public partial ConnectionType SelectedType { get; set; } = ConnectionType.TcpClient;

    [ObservableProperty]public partial string Host { get; set; } = "127.0.0.1";

    [ObservableProperty]public partial int Port { get; set; } = 2000;

    [ObservableProperty]public partial ByteOrder SelectedByteOrder { get; set; } = ByteOrder.BigEndian;

    [ObservableProperty]public partial int TimeoutSeconds { get; set; } = 10;

    [ObservableProperty]public partial int ReconnectIntervalSeconds { get; set; } = 5;

    [ObservableProperty]public partial int MaxConnections { get; set; } = 10;

    [ObservableProperty]public partial bool NoDelay { get; set; } = false;

    [ObservableProperty]public partial int ReceiveBufferSize { get; set; } = 0;

    [ObservableProperty]public partial int SendBufferSize { get; set; } = 0;

    [ObservableProperty]public partial bool IsTcpClient { get; set; } = true;

    [ObservableProperty]public partial bool IsTcpServer { get; set; } = false;

    [ObservableProperty]public partial bool IsUdp { get; set; } = false;
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

