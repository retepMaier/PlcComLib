using CommunityToolkit.Mvvm.ComponentModel;
using PlcComLib.DataTypes;
using PlcComLib.WpfTester.Models;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class SelectableTelegramViewModel : ObservableObject
{
    private const string NoFieldSelected = "(none)";

    public TelegramDefinitionModel Telegram { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [ObservableProperty] public partial long MessageId { get; set; }
    [ObservableProperty] public partial int MessageIdByteOffset { get; set; }
    [ObservableProperty] public partial S7DataType MessageIdDataType { get; set; } = S7DataType.Word;

    /// <summary>
    /// Name of the telegram field that carries the MessageId.
    /// When set to a valid field name the <see cref="MessageIdByteOffset"/> and
    /// <see cref="MessageIdDataType"/> are derived automatically from that field's
    /// position and type.  Set to <c>"(none)"</c> to use the manual values instead.
    /// </summary>
    [ObservableProperty] public partial string MessageIdFieldName { get; set; } = NoFieldSelected;

    [ObservableProperty] public partial int MessageLength { get; set; }
    [ObservableProperty] public partial int LengthByteOffset { get; set; } = -1;
    [ObservableProperty] public partial S7DataType LengthDataType { get; set; } = S7DataType.Word;

    /// <summary>
    /// Name of the telegram field that carries the embedded length value.
    /// When set to a valid field name the <see cref="LengthByteOffset"/> and
    /// <see cref="LengthDataType"/> are derived automatically.
    /// Set to <c>"(none)"</c> to disable length-field validation (<see cref="LengthByteOffset"/> = -1).
    /// </summary>
    [ObservableProperty] public partial string LengthFieldName { get; set; } = NoFieldSelected;

    /// <summary>
    /// Field names available for selection in the MessageId / Length field pickers.
    /// The first entry is always <c>"(none)"</c> which means no field is selected.
    /// </summary>
    public IReadOnlyList<string> AvailableFieldNames =>
        new[] { NoFieldSelected }.Concat(Telegram.Fields.Select(f => f.Name)).ToList();

    public SelectableTelegramViewModel(TelegramDefinitionModel telegram)
    {
        Telegram = telegram;
    }

    partial void OnMessageIdFieldNameChanged(string value)
    {
        if (value == NoFieldSelected) return;
        int offset = 0;
        foreach (var f in Telegram.Fields)
        {
            if (f.Name == value)
            {
                MessageIdByteOffset = offset;
                MessageIdDataType   = f.DataType;
                return;
            }
            offset += S7TypeConverter.GetWireSize(f.DataType, f.MaxStringLength, f.RawByteCount);
        }
    }

    partial void OnLengthFieldNameChanged(string value)
    {
        if (value == NoFieldSelected)
        {
            LengthByteOffset = -1;
            return;
        }
        int offset = 0;
        foreach (var f in Telegram.Fields)
        {
            if (f.Name == value)
            {
                LengthByteOffset = offset;
                LengthDataType   = f.DataType;
                return;
            }
            offset += S7TypeConverter.GetWireSize(f.DataType, f.MaxStringLength, f.RawByteCount);
        }
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
