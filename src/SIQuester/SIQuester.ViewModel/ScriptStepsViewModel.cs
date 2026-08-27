using SIPackages;
using SIPackages.Core;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Owns editable wrappers for the ordered steps in one canonical question script.
/// </summary>
public sealed class ScriptStepsViewModel : ObservableCollection<ScriptStepViewModel>
{
    private readonly QuestionViewModel _question;
    private readonly Script? _script;

    public SimpleCommand AddStep { get; }

    public ScriptStepsViewModel(QuestionViewModel question, Script? script)
    {
        _question = question;
        _script = script;

        if (script != null)
        {
            foreach (var step in script.Steps)
            {
                Items.Add(new ScriptStepViewModel(this, question, step));
            }
        }

        AddStep = new SimpleCommand(AddStep_Executed) { CanBeExecuted = script != null };
        CollectionChanged += ScriptStepsViewModel_CollectionChanged;
        UpdateCommands();
    }

    internal void RemoveStep(ScriptStepViewModel step)
    {
        var index = IndexOf(step);

        if (index > -1)
        {
            RemoveAt(index);
        }
    }

    internal void MoveStep(ScriptStepViewModel step, int offset)
    {
        var oldIndex = IndexOf(step);
        var newIndex = oldIndex + offset;

        if (oldIndex < 0 || newIndex < 0 || newIndex >= Count)
        {
            return;
        }

        Move(oldIndex, newIndex);
    }

    protected override void ClearItems()
    {
        while (Count > 0)
        {
            RemoveAt(Count - 1);
        }
    }

    private void AddStep_Executed(object? arg)
    {
        if (_script == null)
        {
            return;
        }

        var step = new Step { Type = StepTypes.ShowContent };
        step.Parameters[StepParameterNames.Content] = new StepParameter
        {
            Type = StepParameterTypes.Content,
            ContentValue = new List<ContentItem>(),
        };

        Add(new ScriptStepViewModel(this, _question, step));
    }

    private void ScriptStepsViewModel_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_script == null)
        {
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    var index = e.NewStartingIndex;

                    foreach (var step in e.NewItems.Cast<ScriptStepViewModel>())
                    {
                        _script.Steps.Insert(index++, step.Model);
                    }
                }

                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null && e.OldStartingIndex > -1)
                {
                    for (var i = 0; i < e.OldItems.Count; i++)
                    {
                        _script.Steps.RemoveAt(e.OldStartingIndex);
                    }
                }

                break;

            case NotifyCollectionChangedAction.Move:
                if (e.OldStartingIndex > -1 && e.NewStartingIndex > -1)
                {
                    var step = _script.Steps[e.OldStartingIndex];
                    _script.Steps.RemoveAt(e.OldStartingIndex);
                    _script.Steps.Insert(e.NewStartingIndex, step);
                }

                break;

            case NotifyCollectionChangedAction.Replace:
                if (e.NewItems != null && e.NewStartingIndex > -1)
                {
                    for (var i = 0; i < e.NewItems.Count; i++)
                    {
                        _script.Steps[e.NewStartingIndex + i] = ((ScriptStepViewModel)e.NewItems[i]!).Model;
                    }
                }

                break;

            case NotifyCollectionChangedAction.Reset:
                _script.Steps.Clear();
                break;
        }

        UpdateCommands();
        _question.NotifyQuestionTextChanged();
    }

    private void UpdateCommands()
    {
        for (var i = 0; i < Count; i++)
        {
            this[i].UpdateCommandStates(i, Count);
        }
    }
}
