using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.WpfTester.Models;

public sealed partial class TelegramFieldDefinition : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial S7DataType DataType { get; set; } = S7DataType.Word;
    [ObservableProperty] public partial byte MaxStringLength { get; set; } = 0;
    [ObservableProperty] public partial int RawByteCount { get; set; } = 0;
    [ObservableProperty] public partial string DefaultValue { get; set; } = string.Empty;
}

public sealed partial class TelegramDefinitionModel : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    public ObservableCollection<TelegramFieldDefinition> Fields { get; set; } = [];

    public TelegramDefinitionModel DeepCopy()
    {
        var copy = new TelegramDefinitionModel
        {
            Id = Id,
            Name = Name,
        };
        foreach (var f in Fields)
        {
            copy.Fields.Add(new TelegramFieldDefinition
            {
                Name = f.Name,
                DataType = f.DataType,
                MaxStringLength = f.MaxStringLength,
                RawByteCount = f.RawByteCount,
                DefaultValue = f.DefaultValue,
            });
        }
        return copy;
    }
}

public sealed partial class ConnectionTelegramEntry : ObservableObject
{
    public ConnectionTelegramEntry(TelegramDefinitionModel telegram)
    {
        Telegram = telegram;
    }

    public TelegramDefinitionModel Telegram { get; }

    [ObservableProperty] public partial long MessageId { get; set; } = 0;
    [ObservableProperty] public partial int MessageIdByteOffset { get; set; } = 0;
    [ObservableProperty] public partial S7DataType MessageIdDataType { get; set; } = S7DataType.Word;
    [ObservableProperty] public partial int MessageLength { get; set; } = 0;
    [ObservableProperty] public partial int LengthByteOffset { get; set; } = -1;
    [ObservableProperty] public partial S7DataType LengthDataType { get; set; } = S7DataType.Word;

    public TelegramDefinition ToTelegramDefinition()
    {
        var def = new TelegramDefinition
        {
            Id = Telegram.Id,
            Name = Telegram.Name,
            MessageId = MessageId,
            MessageIdByteOffset = MessageIdByteOffset,
            MessageIdDataType = MessageIdDataType,
            ConfiguredWireSize = MessageLength,
            LengthByteOffset = LengthByteOffset,
            LengthDataType = LengthDataType,
        };
        foreach (var f in Telegram.Fields)
        {
            def.Fields.Add(new TelegramField
            {
                Name = f.Name,
                DataType = f.DataType,
                MaxStringLength = f.MaxStringLength,
                RawByteCount = f.RawByteCount,
            });
        }
        return def;
    }

    public ConnectionTelegramEntry DeepCopy()
    {
        return new ConnectionTelegramEntry(Telegram.DeepCopy())
        {
            MessageId = MessageId,
            MessageIdByteOffset = MessageIdByteOffset,
            MessageIdDataType = MessageIdDataType,
            MessageLength = MessageLength,
            LengthByteOffset = LengthByteOffset,
            LengthDataType = LengthDataType,
        };
    }
}

public sealed partial class ConnectionSettings : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial ConnectionType Type { get; set; } = ConnectionType.TcpClient;
    [ObservableProperty] public partial string Host { get; set; } = "127.0.0.1";
    [ObservableProperty] public partial int Port { get; set; } = 2000;
    [ObservableProperty] public partial ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;
    [ObservableProperty] public partial int TimeoutSeconds { get; set; } = 10;
    [ObservableProperty] public partial int ReconnectIntervalSeconds { get; set; } = 5;
    [ObservableProperty] public partial int MaxConnections { get; set; } = 10;
    [ObservableProperty] public partial bool NoDelay { get; set; } = false;
    [ObservableProperty] public partial int ReceiveBufferSize { get; set; } = 0;
    [ObservableProperty] public partial int SendBufferSize { get; set; } = 0;
    public ObservableCollection<ConnectionTelegramEntry> Telegrams { get; set; } = [];
}
