using SIPackages;
using SIPackages.Core;

namespace SIQuester.ViewModel;

/// <summary>
/// Represents step parameter view model.
/// </summary>
public sealed class StepParameterViewModel : ModelViewBase
{
    public StepParameter Model { get; }

    public ContentItemsViewModel? ContentValue { get; }

    public NumberSetEditorNewViewModel? NumberSetValue { get; }

    public StepParametersViewModel? GroupValue { get; }

    public bool UsesSimpleValue => Model.IsRef
        || Model.Type != StepParameterTypes.Content
            && Model.Type != StepParameterTypes.Group
            && Model.Type != StepParameterTypes.NumberSet;

    public bool UsesContentValue => !Model.IsRef && Model.Type == StepParameterTypes.Content;

    public bool UsesGroupValue => !Model.IsRef && Model.Type == StepParameterTypes.Group;

    public bool UsesNumberSetValue => !Model.IsRef && Model.Type == StepParameterTypes.NumberSet;

    public StepParameterViewModel(QuestionViewModel question, StepParameter stepParameter, bool isTopLevel = true)
    {
        Model = stepParameter;

        if (!stepParameter.IsRef
            && stepParameter.Type == StepParameterTypes.Content
            && stepParameter.ContentValue != null)
        {
            ContentValue = new ContentItemsViewModel(question, stepParameter.ContentValue!, isTopLevel);
        }
        else if (stepParameter.Type == StepParameterTypes.NumberSet && stepParameter.NumberSetValue != null)
        {
            NumberSetValue = new NumberSetEditorNewViewModel(stepParameter.NumberSetValue);
        }
        else if (stepParameter.Type == StepParameterTypes.Group && stepParameter.GroupValue != null)
        {
            GroupValue = new StepParametersViewModel(question, stepParameter.GroupValue);
        }
    }
}
