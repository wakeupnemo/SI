using SIPackages;
using System.ComponentModel;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Exposes one canonical question script step to framework-neutral editor views.
/// </summary>
public sealed class ScriptStepViewModel
{
    private readonly ScriptStepsViewModel _owner;
    private readonly QuestionViewModel _question;

    public Step Model { get; }

    public StepParametersViewModel Parameters { get; }

    public SimpleCommand Remove { get; }

    public SimpleCommand MoveUp { get; }

    public SimpleCommand MoveDown { get; }

    public ScriptStepViewModel(ScriptStepsViewModel owner, QuestionViewModel question, Step step)
    {
        _owner = owner;
        _question = question;
        Model = step;
        Parameters = new StepParametersViewModel(question, step.Parameters, contentIsTopLevel: true);
        Remove = new SimpleCommand(_ => _owner.RemoveStep(this));
        MoveUp = new SimpleCommand(_ => _owner.MoveStep(this, -1));
        MoveDown = new SimpleCommand(_ => _owner.MoveStep(this, 1));
        Model.PropertyChanged += Model_PropertyChanged;
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Step.Type))
        {
            _question.NotifyQuestionTextChanged();
        }
    }

    internal void UpdateCommandStates(int index, int count)
    {
        MoveUp.CanBeExecuted = index > 0;
        MoveDown.CanBeExecuted = index > -1 && index < count - 1;
    }
}
