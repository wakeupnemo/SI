using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class DocumentEditorView : UserControl
{
    public DocumentEditorView()
    {
        InitializeComponent();
        KeyDown += DocumentEditorView_KeyDown;
    }

    private void DocumentEditorView_KeyDown(object? sender, KeyEventArgs e)
    {
        var primaryModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (!e.Handled
            && primaryModifier
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void Navigator_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is QDocument document
            && sender is TreeView { SelectedItem: IItemViewModel selectedItem })
        {
            document.ActiveNode = selectedItem;
        }
    }
}
