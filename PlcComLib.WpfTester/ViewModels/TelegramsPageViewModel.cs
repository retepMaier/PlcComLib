using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PlcComLib.DataTypes;
using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.Services;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class TelegramsPageViewModel : ObservableObject
{
    private readonly ITelegramLibraryService _telegramLibrary;

    public ObservableCollection<TelegramDefinitionModel> Telegrams => _telegramLibrary.Telegrams;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTelegram))]
    private TelegramDefinitionModel? _selectedTelegram;

    public bool HasSelectedTelegram => SelectedTelegram is not null;

    public ObservableCollection<TelegramFieldDefinition> CurrentTelegramFields { get; } = [];

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

    public IReadOnlyList<S7DataType> DataTypes { get; } = Enum.GetValues<S7DataType>();

    public TelegramsPageViewModel(ITelegramLibraryService telegramLibrary)
    {
        _telegramLibrary = telegramLibrary;
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
        _telegramLibrary.Add(t);
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
        _telegramLibrary.Remove(t);
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
            await _telegramLibrary.ImportAsync(dialog.FileName);
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
            await _telegramLibrary.ExportAsync(dialog.FileName);
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
