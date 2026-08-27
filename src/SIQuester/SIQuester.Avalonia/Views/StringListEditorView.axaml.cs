using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class StringListEditorView : UserControl
{
    private bool _isRestoringSelection;

    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<StringListEditorView, string>(nameof(Header), string.Empty);

    public static readonly StyledProperty<ItemsViewModel<string>?> EditorProperty =
        AvaloniaProperty.Register<StringListEditorView, ItemsViewModel<string>?>(nameof(Editor));

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public ItemsViewModel<string>? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public StringListEditorView() => InitializeComponent();

    private void ItemsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringSelection || sender is not ListBox listBox || Editor is not { } editor)
        {
            return;
        }

        if (listBox.SelectedIndex >= 0)
        {
            editor.CurrentPosition = listBox.SelectedIndex;
            return;
        }

        if (!editor.HasCurrentItem)
        {
            return;
        }

        // Replacing the selected string raises a collection Replace notification. Avalonia's
        // selector can briefly clear selection while processing it; propagating that transient
        // state disables the adjacent TextBox and drops keyboard focus after every character.
        _isRestoringSelection = true;

        try
        {
            listBox.SelectedIndex = editor.CurrentPosition;
        }
        finally
        {
            _isRestoringSelection = false;
        }
    }
}
