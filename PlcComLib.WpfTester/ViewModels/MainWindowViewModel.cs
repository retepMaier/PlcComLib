using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "PlcComLib WPF Tester";
}
