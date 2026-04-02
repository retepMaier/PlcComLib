using CommunityToolkit.Mvvm.ComponentModel;

namespace PlcComLib.WpfTester.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]    public partial string Title { get; set; } = "PlcComLib WPF Tester";
}
