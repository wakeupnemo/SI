using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SIQuester.ViewModel;
using System.Windows.Input;

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
        KeyDown += MainWindow_KeyDown;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled
            || DataContext is not MainViewModel { ActiveDocument: { } document }
            || FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        var primaryModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        ICommand? command = null;

        if (primaryModifier && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            command = e.Key switch
            {
                Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Shift) => document.OperationsManager.Redo,
                Key.Z => document.OperationsManager.Undo,
                Key.Y => document.OperationsManager.Redo,
                Key.X => document.Cut,
                Key.C => document.Copy,
                Key.V => document.Paste,
                _ => null,
            };
        }
        else if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            command = document.Delete;
        }

        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            e.Handled = true;
        }
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
            await _beforeClose(CancellationToken.None);

            if (await viewModel.TryCloseAsync())
            {
                _closeApproved = true;
                Close();
            }
        }
        catch (Exception exception)
        {
            await viewModel.ReportShutdownFailureAsync(exception);
        }
        finally
        {
            _closeInProgress = false;
        }
    }
}
