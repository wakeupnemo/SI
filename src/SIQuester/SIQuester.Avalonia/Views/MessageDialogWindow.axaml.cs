using Avalonia.Controls;
using Avalonia.Interactivity;
using SIQuester.ViewModel.Contracts;

namespace SIQuester.Avalonia.Views;

public enum MessageDialogKind
{
    SaveChanges,
    Confirm,
    Message,
}

public partial class MessageDialogWindow : Window
{
    private object? _result;

    public MessageDialogWindow()
    {
        InitializeComponent();
    }

    public MessageDialogWindow(string message, MessageDialogKind kind, bool allowCancel = true) : this()
    {
        MessageText.Text = message;
        CancelButton.IsVisible = kind == MessageDialogKind.SaveChanges && allowCancel;
        DiscardButton.IsVisible = kind == MessageDialogKind.SaveChanges;
        SaveButton.IsVisible = kind == MessageDialogKind.SaveChanges;
        NoButton.IsVisible = kind == MessageDialogKind.Confirm;
        YesButton.IsVisible = kind == MessageDialogKind.Confirm;
        OkButton.IsVisible = kind == MessageDialogKind.Message;
    }

    public async ValueTask<SaveChangesDecision> ShowSaveChangesAsync(Window owner)
    {
        await ShowDialog(owner);
        return _result as SaveChangesDecision? ?? SaveChangesDecision.Cancel;
    }

    public async ValueTask<bool> ShowConfirmationAsync(Window owner)
    {
        await ShowDialog(owner);
        return _result as bool? ?? false;
    }

    public async ValueTask ShowMessageAsync(Window owner) => await ShowDialog(owner);

    private void Save_Click(object? sender, RoutedEventArgs e) => CloseWith(SaveChangesDecision.Save);
    private void Discard_Click(object? sender, RoutedEventArgs e) => CloseWith(SaveChangesDecision.Discard);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(SaveChangesDecision.Cancel);
    private void Yes_Click(object? sender, RoutedEventArgs e) => CloseWith(true);
    private void No_Click(object? sender, RoutedEventArgs e) => CloseWith(false);
    private void Ok_Click(object? sender, RoutedEventArgs e) => CloseWith(true);

    private void CloseWith(object result)
    {
        _result = result;
        Close();
    }
}
