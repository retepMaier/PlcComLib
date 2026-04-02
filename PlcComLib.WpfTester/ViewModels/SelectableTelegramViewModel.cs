using CommunityToolkit.Mvvm.ComponentModel;
using PlcComLib.WpfTester.Models;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class SelectableTelegramViewModel : ObservableObject
{
    public TelegramDefinitionModel Telegram { get; }

    [ObservableProperty]
    private bool _isSelected;

    public SelectableTelegramViewModel(TelegramDefinitionModel telegram)
    {
        Telegram = telegram;
    }
}
