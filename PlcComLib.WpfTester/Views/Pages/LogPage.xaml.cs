using System.Collections.Specialized;
using PlcComLib.WpfTester.ViewModels;

namespace PlcComLib.WpfTester.Views.Pages;

public partial class LogPage : System.Windows.Controls.Page
{
    private readonly LogPageViewModel _viewModel;

    public LogPage(LogPageViewModel viewModel)
    {
        DataContext = viewModel;
        _viewModel = viewModel;
        InitializeComponent();
        _viewModel.Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel.AutoScroll)
            LogScrollViewer.ScrollToBottom();
    }
}
