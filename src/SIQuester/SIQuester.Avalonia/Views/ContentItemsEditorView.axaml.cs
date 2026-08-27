using Avalonia;
using Avalonia.Controls;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class ContentItemsEditorView : UserControl
{
    public static readonly StyledProperty<ContentItemsViewModel?> EditorProperty =
        AvaloniaProperty.Register<ContentItemsEditorView, ContentItemsViewModel?>(nameof(Editor));

    public ContentItemsViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public ContentItemsEditorView() => InitializeComponent();
}
