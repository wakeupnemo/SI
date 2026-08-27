using Avalonia;
using Avalonia.Controls;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class StepParametersEditorView : UserControl
{
    public static readonly StyledProperty<StepParametersViewModel?> EditorProperty =
        AvaloniaProperty.Register<StepParametersEditorView, StepParametersViewModel?>(nameof(Editor));

    public StepParametersViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public StepParametersEditorView() => InitializeComponent();
}
