using Avalonia;
using Avalonia.Controls;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class ScenarioEditorView : UserControl
{
    public static readonly StyledProperty<QuestionViewModel?> QuestionProperty =
        AvaloniaProperty.Register<ScenarioEditorView, QuestionViewModel?>(nameof(Question));

    public QuestionViewModel? Question
    {
        get => GetValue(QuestionProperty);
        set => SetValue(QuestionProperty, value);
    }

    public ScenarioEditorView() => InitializeComponent();
}
