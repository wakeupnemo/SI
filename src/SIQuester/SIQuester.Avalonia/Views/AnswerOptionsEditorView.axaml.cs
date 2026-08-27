using Avalonia;
using Avalonia.Controls;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class AnswerOptionsEditorView : UserControl
{
    public static readonly StyledProperty<QuestionViewModel?> QuestionProperty =
        AvaloniaProperty.Register<AnswerOptionsEditorView, QuestionViewModel?>(nameof(Question));

    public static readonly StyledProperty<StepParameterViewModel?> OptionsProperty =
        AvaloniaProperty.Register<AnswerOptionsEditorView, StepParameterViewModel?>(nameof(Options));

    public QuestionViewModel? Question
    {
        get => GetValue(QuestionProperty);
        set => SetValue(QuestionProperty, value);
    }

    public StepParameterViewModel? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public AnswerOptionsEditorView() => InitializeComponent();
}
