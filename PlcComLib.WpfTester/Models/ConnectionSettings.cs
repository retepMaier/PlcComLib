using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.WpfTester.Models;

public sealed class TelegramFieldDefinition
{
    public string Name { get; set; } = string.Empty;
    public S7DataType DataType { get; set; } = S7DataType.Word;
    public byte MaxStringLength { get; set; } = 0;
    public int RawByteCount { get; set; } = 0;
    public string DefaultValue { get; set; } = string.Empty;
}

public sealed class TelegramDefinitionModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public long MessageId { get; set; } = 0;
    public int MessageIdByteOffset { get; set; } = 0;

    public long MessageLength { get; set; } = 0;
    public int MessageLengthByteOffset { get; set; } = 2;
    public S7DataType MessageIdDataType { get; set; } = S7DataType.Word;

    public S7DataType MessageLengthDataType { get; set; } = S7DataType.Int;
    public List<TelegramFieldDefinition> Fields { get; set; } = [];

    public TelegramDefinition ToTelegramDefinition()
    {
        var def = new TelegramDefinition
        {
            Id = Id,
            Name = Name,
            MessageId = MessageId,
            MessageIdByteOffset = MessageIdByteOffset,
            MessageIdDataType = MessageIdDataType,
            LengthByteOffset = MessageLengthByteOffset,
            LengthDataType = MessageLengthDataType,
        };
        foreach (var f in Fields)
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

    public TelegramDefinitionModel DeepCopy()
    {
        return new TelegramDefinitionModel
        {
            Id = Id,
            Name = Name,
            MessageId = MessageId,
            MessageIdByteOffset = MessageIdByteOffset,
            MessageIdDataType = MessageIdDataType,
            MessageLengthByteOffset = MessageLengthByteOffset,
            MessageLengthDataType = MessageLengthDataType,

            Fields = Fields.Select(f => new TelegramFieldDefinition
            {
                Name = f.Name,
                DataType = f.DataType,
                MaxStringLength = f.MaxStringLength,
                RawByteCount = f.RawByteCount,
                DefaultValue = f.DefaultValue,
            }).ToList(),
        };
    }
}

public sealed class ConnectionSettings
{
    public string Name { get; set; } = string.Empty;
    public ConnectionType Type { get; set; } = ConnectionType.TcpClient;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 2000;
    public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;
    public int TimeoutSeconds { get; set; } = 10;
    public int ReconnectIntervalSeconds { get; set; } = 5;
    public int MaxConnections { get; set; } = 10;
    public bool NoDelay { get; set; } = false;
    public int ReceiveBufferSize { get; set; } = 0;
    public int SendBufferSize { get; set; } = 0;
    public List<TelegramDefinitionModel> Telegrams { get; set; } = [];
}
