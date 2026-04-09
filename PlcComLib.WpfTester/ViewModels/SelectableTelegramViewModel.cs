using CommunityToolkit.Mvvm.ComponentModel;
using PlcComLib.DataTypes;
using PlcComLib.WpfTester.Models;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class SelectableTelegramViewModel : ObservableObject
{
    public TelegramDefinitionModel Telegram { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [ObservableProperty] public partial long MessageId { get; set; }
    [ObservableProperty] public partial int MessageIdByteOffset { get; set; }
    [ObservableProperty] public partial S7DataType MessageIdDataType { get; set; } = S7DataType.Word;
    [ObservableProperty] public partial int MessageLength { get; set; }
    [ObservableProperty] public partial int LengthByteOffset { get; set; } = -1;
    [ObservableProperty] public partial S7DataType LengthDataType { get; set; } = S7DataType.Word;

    public SelectableTelegramViewModel(TelegramDefinitionModel telegram)
    {
        Telegram = telegram;
    }

    public ConnectionTelegramEntry ToConnectionTelegramEntry()
    {
        var entry = new ConnectionTelegramEntry(Telegram.DeepCopy())
        {
            MessageId = MessageId,
            MessageIdByteOffset = MessageIdByteOffset,
            MessageIdDataType = MessageIdDataType,
            MessageLength = MessageLength,
            LengthByteOffset = LengthByteOffset,
            LengthDataType = LengthDataType,
        };
        return entry;
    }
}
