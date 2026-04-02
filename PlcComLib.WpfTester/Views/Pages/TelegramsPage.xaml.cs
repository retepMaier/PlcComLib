using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlcComLib.WpfTester.ViewModels;

namespace PlcComLib.WpfTester.Views.Pages;

public partial class TelegramsPage : System.Windows.Controls.Page
{
    public TelegramsPage(TelegramsPageViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    // Single-click cell editing for the field DataGrid.
    private void FieldCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridCell { IsEditing: false, IsReadOnly: false } cell)
        {
            cell.Focus();
            if (ItemsControl.ItemsControlFromItemContainer(cell) is DataGrid grid)
                grid.BeginEdit();
        }
    }
}
