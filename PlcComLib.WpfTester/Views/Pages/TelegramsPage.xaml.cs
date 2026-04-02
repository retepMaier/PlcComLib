using PlcComLib.WpfTester.ViewModels;

namespace PlcComLib.WpfTester.Views.Pages;

public partial class TelegramsPage : System.Windows.Controls.Page
{
    public TelegramsPage(TelegramsPageViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
