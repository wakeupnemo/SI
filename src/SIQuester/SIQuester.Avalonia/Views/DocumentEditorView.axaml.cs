using Avalonia.Controls;
using Avalonia.Controls.Selection;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class DocumentEditorView : UserControl
{
    public DocumentEditorView() => InitializeComponent();

    private void Navigator_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is QDocument document
            && sender is TreeView { SelectedItem: IItemViewModel selectedItem })
        {
            document.ActiveNode = selectedItem;
        }
    }
}
