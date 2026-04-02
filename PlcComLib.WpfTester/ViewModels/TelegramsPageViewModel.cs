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

    public ObservableCollection<TelegramFieldDefinition> CurrentTelegramFields { get; } = [];

    [ObservableProperty]    public partial string NewTelegramName { get; set; } = string.Empty;

    [ObservableProperty]    public partial long NewTelegramMessageId { get; set; } = 0;

    [ObservableProperty]    public partial int NewTelegramMessageIdOffset { get; set; } = 0;

    [ObservableProperty]    public partial S7DataType NewTelegramMessageIdType { get; set; } = S7DataType.Word;

    [ObservableProperty]    public partial string NewFieldName { get; set; } = string.Empty;

    [ObservableProperty]    public partial S7DataType NewFieldDataType { get; set; } = S7DataType.Word;
    public IReadOnlyList<S7DataType> DataTypes { get; } = Enum.GetValues<S7DataType>();

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
        telegramLibrary.Add(t);
        SelectedTelegram = t;
        CurrentTelegramFields.Clear();
        NewTelegramName = string.Empty;
        NewTelegramMessageId = 0;
        NewTelegramMessageIdOffset = 0;
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
