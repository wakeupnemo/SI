using Avalonia.Controls;
using Avalonia.Interactivity;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closeInProgress;
    private readonly Func<CancellationToken, ValueTask> _beforeClose;

    public MainWindow() : this(_ => ValueTask.CompletedTask) { }

    public MainWindow(Func<CancellationToken, ValueTask> beforeClose)
    {
        _beforeClose = beforeClose;
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
                await _beforeClose(CancellationToken.None);
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
