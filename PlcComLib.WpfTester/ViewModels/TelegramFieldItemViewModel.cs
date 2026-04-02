using CommunityToolkit.Mvvm.ComponentModel;
using PlcComLib.DataTypes;
using PlcComLib.WpfTester.Models;

namespace PlcComLib.WpfTester.ViewModels;

/// <summary>
/// Observable wrapper around a <see cref="TelegramFieldDefinition"/> that allows
/// in-place editing in the field table (name, data type, default value, length).
/// All property setters write through to the underlying model object so that no
/// explicit "save" step is needed.
/// </summary>
public sealed partial class TelegramFieldItemViewModel : ObservableObject
{
    // Guards against side-effect callbacks during construction.
    private bool _initializing;

    public TelegramFieldDefinition Field { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsLength))]
    public partial S7DataType DataType { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string DefaultValue { get; set; }

    [ObservableProperty]
    public partial int Length { get; set; }

    /// <summary>
    /// True for data types that require an explicit length/size parameter
    /// (S7String, S7WString, CharArray, Raw).
    /// </summary>
    public bool NeedsLength =>
        DataType is S7DataType.S7String
                 or S7DataType.S7WString
                 or S7DataType.CharArray
                 or S7DataType.Raw;

    public TelegramFieldItemViewModel(TelegramFieldDefinition field)
    {
        _initializing = true;
        Field = field;
        Name = field.Name;
        DataType = field.DataType;
        DefaultValue = field.DefaultValue;
        Length = field.DataType == S7DataType.Raw ? field.RawByteCount : field.MaxStringLength;
        _initializing = false;
    }

    partial void OnNameChanged(string value)
    {
        if (!_initializing) Field.Name = value;
    }

    partial void OnDataTypeChanged(S7DataType value)
    {
        Field.DataType = value;
        if (_initializing) return;
        if (!NeedsLength)
        {
            Field.MaxStringLength = 0;
            Field.RawByteCount = 0;
            Length = 0;
        }
        else
        {
            SyncLength(Length);
        }
    }

    partial void OnDefaultValueChanged(string value)
    {
        if (!_initializing) Field.DefaultValue = value;
    }

    partial void OnLengthChanged(int value)
    {
        if (!_initializing) SyncLength(value);
    }

    private void SyncLength(int value)
    {
        if (DataType == S7DataType.Raw)
        {
            Field.RawByteCount = Math.Max(0, value);
            Field.MaxStringLength = 0;
        }
        else
        {
            Field.MaxStringLength = (byte)Math.Clamp(value, 0, 255);
            Field.RawByteCount = 0;
        }
    }
}
