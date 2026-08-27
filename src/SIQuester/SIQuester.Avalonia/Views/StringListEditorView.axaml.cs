using Avalonia;
using Avalonia.Controls;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class StringListEditorView : UserControl
{
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
}
