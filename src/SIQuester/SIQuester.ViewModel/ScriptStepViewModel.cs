using SIPackages;

namespace SIQuester.ViewModel;

/// <summary>
/// Exposes one canonical question script step to framework-neutral editor views.
/// </summary>
public sealed class ScriptStepViewModel
{
    public Step Model { get; }

    public StepParametersViewModel Parameters { get; }

    public ScriptStepViewModel(QuestionViewModel question, Step step)
    {
        Model = step;
        Parameters = new StepParametersViewModel(question, step.Parameters, contentIsTopLevel: true);
    }
}
