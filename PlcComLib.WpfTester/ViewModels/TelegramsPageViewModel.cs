using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PlcComLib.DataTypes;
using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.Services;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class TelegramsPageViewModel(ITelegramLibraryService telegramLibrary) : ObservableObject
{
    public ObservableCollection<TelegramDefinitionModel> Telegrams => telegramLibrary.Telegrams;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTelegram))]
    public partial TelegramDefinitionModel? SelectedTelegram { get; set; }

    public bool HasSelectedTelegram => SelectedTelegram is not null;

    public ObservableCollection<TelegramFieldItemViewModel> CurrentTelegramFields { get; } = [];

    [ObservableProperty] public partial string NewTelegramName { get; set; } = string.Empty;

    [ObservableProperty] public partial string NewFieldName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewFieldNeedsLength))]
    public partial S7DataType NewFieldDataType { get; set; } = S7DataType.Word;

    [ObservableProperty] public partial string NewFieldDefaultValue { get; set; } = string.Empty;

    [ObservableProperty] public partial int NewFieldLength { get; set; } = 0;

    public bool NewFieldNeedsLength =>
        NewFieldDataType is S7DataType.S7String
                         or S7DataType.S7WString
                         or S7DataType.CharArray
                         or S7DataType.Raw;

    public IReadOnlyList<S7DataType> DataTypes { get; } = Enum.GetValues<S7DataType>();

    [RelayCommand]
    private void AddTelegram()
    {
        if (string.IsNullOrWhiteSpace(NewTelegramName)) return;
        var t = new TelegramDefinitionModel { Name = NewTelegramName };
        telegramLibrary.Add(t);
        SelectedTelegram = t;
        CurrentTelegramFields.Clear();
        NewTelegramName = string.Empty;
    }

    [RelayCommand]
    private void RemoveTelegram(TelegramDefinitionModel? t)
    {
        if (t is null) return;
        telegramLibrary.Remove(t);
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
            CurrentTelegramFields.Add(new TelegramFieldItemViewModel(f));
    }

    [RelayCommand]
    private void AddField()
    {
        if (SelectedTelegram is null || string.IsNullOrWhiteSpace(NewFieldName)) return;
        var field = new TelegramFieldDefinition
        {
            Name = NewFieldName,
            DataType = NewFieldDataType,
            DefaultValue = NewFieldDefaultValue,
        };
        if (NewFieldNeedsLength)
        {
            if (NewFieldDataType == S7DataType.Raw)
                field.RawByteCount = Math.Max(0, NewFieldLength);
            else
                field.MaxStringLength = (byte)Math.Clamp(NewFieldLength, 0, 255);
        }
        SelectedTelegram.Fields.Add(field);
        CurrentTelegramFields.Add(new TelegramFieldItemViewModel(field));
        NewFieldName = string.Empty;
        NewFieldDefaultValue = string.Empty;
        NewFieldLength = 0;
    }

    [RelayCommand]
    private void RemoveField(TelegramFieldItemViewModel? item)
    {
        if (item is null || SelectedTelegram is null) return;
        SelectedTelegram.Fields.Remove(item.Field);
        CurrentTelegramFields.Remove(item);
    }

    [RelayCommand]
    private void MoveFieldUp(TelegramFieldItemViewModel? item)
    {
        if (item is null || SelectedTelegram is null) return;
        var idx = CurrentTelegramFields.IndexOf(item);
        if (idx <= 0) return;
        var field = SelectedTelegram.Fields[idx];
        CurrentTelegramFields.Move(idx, idx - 1);
        SelectedTelegram.Fields.RemoveAt(idx);
        SelectedTelegram.Fields.Insert(idx - 1, field);
    }

    [RelayCommand]
    private void MoveFieldDown(TelegramFieldItemViewModel? item)
    {
        if (item is null || SelectedTelegram is null) return;
        var idx = CurrentTelegramFields.IndexOf(item);
        if (idx < 0 || idx >= CurrentTelegramFields.Count - 1) return;
        var field = SelectedTelegram.Fields[idx];
        CurrentTelegramFields.Move(idx, idx + 1);
        SelectedTelegram.Fields.RemoveAt(idx);
        SelectedTelegram.Fields.Insert(idx + 1, field);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Telegrams",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await telegramLibrary.ImportAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Import failed: {ex.Message}",
                "Import Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Telegrams",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = "telegrams.json",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await telegramLibrary.ExportAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Export failed: {ex.Message}",
                "Export Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}

