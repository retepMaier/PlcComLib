using PlcComLib.WpfTester.Models;
using PlcComLib.WpfTester.ViewModels;

namespace PlcComLib.WpfTester.Views.Dialogs;

public partial class AddConnectionDialog : System.Windows.Window
{
    public ConnectionSettings? ResultSettings { get; private set; }
    private readonly AddConnectionDialogViewModel _viewModel;

    public AddConnectionDialog()
    {
        _viewModel = new AddConnectionDialogViewModel();
        DataContext = _viewModel;
        InitializeComponent();
    }

    private void OnCancelClick(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnCreateClick(object sender, System.Windows.RoutedEventArgs e)
    {
        ResultSettings = _viewModel.BuildSettings();
        DialogResult = true;
        Close();
    }
}
