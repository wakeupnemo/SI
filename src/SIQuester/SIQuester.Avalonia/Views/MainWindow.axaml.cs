using Avalonia.Controls;
using Avalonia.Interactivity;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closeInProgress;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        e.Cancel = true;

        if (_closeInProgress)
        {
            return;
        }

        _closeInProgress = true;

        try
        {
            if (await viewModel.TryCloseAsync())
            {
                _closeApproved = true;
                Close();
            }
        }
        finally
        {
            _closeInProgress = false;
        }
    }
}
