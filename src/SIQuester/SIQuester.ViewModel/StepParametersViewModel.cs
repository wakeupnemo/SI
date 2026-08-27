using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Helpers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Represents step parameters view model.
/// </summary>
public sealed class StepParametersViewModel : ObservableCollection<StepParameterRecord>
{
    public const string ReferenceParameterKind = "reference";

    private static readonly string[] KnownParameterKinds =
    [
        StepParameterTypes.Simple,
        StepParameterTypes.Content,
        StepParameterTypes.Group,
        StepParameterTypes.NumberSet,
        ReferenceParameterKind,
    ];

    private readonly QuestionViewModel _question;
    private readonly bool _contentIsTopLevel;
    private string _newParameterName = "";

    public StepParameters Model { get; }

    public bool HasComplexAnswer => Model.ContainsKey(QuestionParameterNames.Answer);

    /// <summary>
    /// Question answer type.
    /// </summary>
    public string AnswerType
    {
        get
        {
            if (!TryGetValue(QuestionParameterNames.AnswerType, out var answerTypeParameter))
            {
                return StepParameterValues.SetAnswerTypeType_Text;
            }

            return answerTypeParameter.Model.SimpleValue;
        }
    }

    public SimpleCommand AddItem { get; }

    public SimpleCommand DeleteItem { get; }

    public SimpleCommand MakeRight { get; }

    /// <summary>
    /// Adds a named parameter of the canonical type supplied as the command argument.
    /// </summary>
    public SimpleCommand AddGenericParameter { get; }

    /// <summary>
    /// Removes the supplied parameter record.
    /// </summary>
    public SimpleCommand DeleteParameter { get; }

    /// <summary>
    /// Renames the supplied parameter to its validated draft key.
    /// </summary>
    public SimpleCommand RenameParameter { get; }

    /// <summary>
    /// Converts the supplied parameter to its explicitly selected canonical kind.
    /// </summary>
    public SimpleCommand ConvertParameter { get; }

    /// <summary>
    /// Gets or sets the exact key used when a generic parameter is added.
    /// </summary>
    public string NewParameterName
    {
        get => _newParameterName;
        set
        {
            if (_newParameterName == value)
            {
                return;
            }

            _newParameterName = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(NewParameterName)));
            UpdateCommands();
        }
    }

    public QuestionViewModel Owner => _question;

    public StepParametersViewModel(
        QuestionViewModel question,
        StepParameters parameters,
        bool? contentIsTopLevel = null)
    {
        _question = question;
        Model = parameters;

        var isTopLevel = contentIsTopLevel ?? question.Model.Parameters == parameters;
        _contentIsTopLevel = isTopLevel;

        foreach (var parameter in parameters)
        {
            InsertSorted(new StepParameterRecord(
                parameter.Key,
                new StepParameterViewModel(question, parameter.Value, isTopLevel)));
        }

        AddItem = new SimpleCommand(AddItem_Executed);
        DeleteItem = new SimpleCommand(DeleteItem_Executed);
        MakeRight = new SimpleCommand(MakeRight_Executed);
        AddGenericParameter = new SimpleCommand(AddGenericParameter_Executed);
        DeleteParameter = new SimpleCommand(DeleteParameter_Executed);
        RenameParameter = new SimpleCommand(RenameParameter_Executed);
        ConvertParameter = new SimpleCommand(ConvertParameter_Executed);

        UpdateCommands();

        CollectionChanged += StepParametersViewModel_CollectionChanged;
    }

    internal void InsertSorted(StepParameterRecord stepParameterRecord)
    {
        PrepareRecord(stepParameterRecord);
        var parameterWeight = GetParameterWeight(stepParameterRecord);

        for (var i = 0; i < Count; i++)
        {
            if (parameterWeight < GetParameterWeight(this[i]))
            {
                Insert(i, stepParameterRecord);
                return;
            }
        }

        Add(stepParameterRecord);
    }

    private static int GetParameterWeight(StepParameterRecord parameter)
    {
        if (parameter.Value.Model.Type == StepParameterTypes.Simple)
        {
            return -40;
        }

        if (parameter.Value.Model.Type == StepParameterTypes.NumberSet)
        {
            return -30;
        }

        if (parameter.Key == QuestionParameterNames.Question)
        {
            return -20;
        }

        if (parameter.Value.Model.Type == StepParameterTypes.Content)
        {
            return -10;
        }

        return 0;
    }

    private void UpdateCommands()
    {
        DeleteItem.CanBeExecuted = Count > 2; // TODO: this is a forced mode for select options. That is not true for other cases
        AddGenericParameter.CanBeExecuted = !string.IsNullOrWhiteSpace(_newParameterName)
            && !Model.ContainsKey(_newParameterName);
    }

    private void AddGenericParameter_Executed(object? arg)
    {
        if (!AddGenericParameter.CanBeExecuted || arg is not string parameterKind)
        {
            return;
        }

        var parameter = parameterKind switch
        {
            StepParameterTypes.Simple => new StepParameter
            {
                Type = StepParameterTypes.Simple,
                SimpleValue = "",
            },
            StepParameterTypes.Content => new StepParameter
            {
                Type = StepParameterTypes.Content,
                ContentValue = new List<ContentItem>(),
            },
            StepParameterTypes.Group => new StepParameter
            {
                Type = StepParameterTypes.Group,
                GroupValue = new StepParameters(),
            },
            StepParameterTypes.NumberSet => new StepParameter
            {
                Type = StepParameterTypes.NumberSet,
                NumberSetValue = new NumberSet(),
            },
            ReferenceParameterKind => new StepParameter
            {
                Type = StepParameterTypes.Simple,
                IsRef = true,
                SimpleValue = "",
            },
            _ => null,
        };

        if (parameter == null)
        {
            return;
        }

        var parameterName = _newParameterName;
        AddParameter(parameterName, new StepParameterViewModel(_question, parameter, _contentIsTopLevel));
        NewParameterName = "";
    }

    private void DeleteParameter_Executed(object? arg)
    {
        if (arg is StepParameterRecord parameter && Contains(parameter))
        {
            Remove(parameter);
        }
    }

    private void RenameParameter_Executed(object? arg)
    {
        if (arg is not StepParameterRecord parameter
            || !Contains(parameter)
            || !parameter.CanRename
            || _question.OwnerTheme?.OwnerRound?.OwnerPackage?.Document is not QDocument document)
        {
            return;
        }

        var index = IndexOf(parameter);
        var renamed = new StepParameterRecord(parameter.DraftKey, parameter.Value);
        using var change = document.OperationsManager.BeginComplexChange();
        RemoveAt(index);
        Insert(index, renamed);
        change.Commit();
    }

    private void ConvertParameter_Executed(object? arg)
    {
        if (arg is not StepParameterRecord parameter
            || !Contains(parameter)
            || !parameter.CanConvert
            || _question.OwnerTheme?.OwnerRound?.OwnerPackage?.Document is not QDocument document)
        {
            return;
        }

        var convertedModel = CreateParameter(parameter.DraftKind, parameter.Value.Model.SimpleValue);

        if (convertedModel == null)
        {
            parameter.ResetDrafts();
            return;
        }

        using var change = document.OperationsManager.BeginComplexChange();
        Remove(parameter);
        InsertSorted(new StepParameterRecord(
            parameter.Key,
            new StepParameterViewModel(_question, convertedModel, _contentIsTopLevel)));
        change.Commit();
    }

    private static StepParameter? CreateParameter(string parameterKind, string previousSimpleValue) =>
        parameterKind switch
        {
            StepParameterTypes.Simple => new StepParameter
            {
                Type = StepParameterTypes.Simple,
                SimpleValue = previousSimpleValue,
            },
            StepParameterTypes.Content => new StepParameter
            {
                Type = StepParameterTypes.Content,
                ContentValue = new List<ContentItem>(),
            },
            StepParameterTypes.Group => new StepParameter
            {
                Type = StepParameterTypes.Group,
                GroupValue = new StepParameters(),
            },
            StepParameterTypes.NumberSet => new StepParameter
            {
                Type = StepParameterTypes.NumberSet,
                NumberSetValue = new NumberSet(),
            },
            ReferenceParameterKind => new StepParameter
            {
                Type = StepParameterTypes.Simple,
                IsRef = true,
                SimpleValue = previousSimpleValue,
            },
            _ => null,
        };

    private void AddItem_Executed(object? arg)
    {
        if (arg is not QuestionViewModel question)
        {
            return;
        }

        var counter = Count;
        var label = IndexLabelHelper.GetIndexLabel(counter);

        var stepParameter = new StepParameter
        {
            Type = StepParameterTypes.Content,
            ContentValue = new List<ContentItem>
            {
                new() { Type = ContentTypes.Text, Value = "" },
            }
        };

        var stepParameterViewModel = new StepParameterViewModel(question, stepParameter, false);
        InsertSorted(new StepParameterRecord(label, stepParameterViewModel));
        UpdateCommands();
    }

    private void DeleteItem_Executed(object? arg)
    {
        var package = _question.OwnerTheme?.OwnerRound?.OwnerPackage;

        if (arg is not StepParameterRecord item || package == null)
        {
            return;
        }

        var rightAnswer = _question.Right.Count > 0 ? _question.Right[0] : "";

        using var change = package.Document.OperationsManager.BeginComplexChange();

        Remove(item);

        var rightIsValid = false;

        for (var i = 0; i < Count; i++)
        {
            var key = IndexLabelHelper.GetIndexLabel(i);
            this[i] = new StepParameterRecord(key, this[i].Value);
            rightIsValid = key == rightAnswer || rightIsValid;
        }

        if (!rightIsValid && _question.Right.Count > 0 && Count > 0)
        {
            _question.Right[0] = this[^1].Key; // Set the last parameter as right answer if the previous one was removed
        }

        UpdateCommands();
        change.Commit();
    }

    private void MakeRight_Executed(object? arg)
    {
        if (arg is not StepParameterRecord item)
        {
            return;
        }

        if (_question.Right.Count == 0)
        {
            _question.Right.Add(item.Key);
        }
        else
        {
            _question.Right[0] = item.Key;
        }
    }

    private void StepParametersViewModel_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    for (int i = e.NewStartingIndex; i < e.NewStartingIndex + e.NewItems.Count; i++)
                    {
                        var item = this[i];
                        PrepareRecord(item);
                        Model[item.Key] = item.Value.Model;
                    }
                }

                break;

            case NotifyCollectionChangedAction.Replace:
                if (e.OldItems != null)
                {
                    foreach (var item in e.OldItems.Cast<StepParameterRecord>())
                    {
                        Model.Remove(item.Key);
                    }
                }

                if (e.NewItems != null)
                {
                    for (int i = e.NewStartingIndex; i < e.NewStartingIndex + e.NewItems.Count; i++)
                    {
                        var item = this[i];
                        PrepareRecord(item);
                        Model[item.Key] = item.Value.Model;
                    }
                }

                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (var item in e.OldItems.Cast<StepParameterRecord>())
                    {
                        Model.Remove(item.Key);
                    }
                }

                break;

            case NotifyCollectionChangedAction.Reset:
                Model.Clear();
                break;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasComplexAnswer)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(AnswerType)));
        RefreshRecordValidation();
        UpdateCommands();
    }

    private void PrepareRecord(StepParameterRecord parameter)
    {
        parameter.AttachValidation(
            candidate => !string.IsNullOrWhiteSpace(candidate)
                && (candidate == parameter.Key || !Model.ContainsKey(candidate)),
            KnownParameterKinds);
    }

    private void RefreshRecordValidation()
    {
        foreach (var parameter in this)
        {
            parameter.RefreshValidation();
        }
    }

    public void AddAnswer(StepParameterViewModel answer) => AddParameter(QuestionParameterNames.Answer, answer);

    public void AddParameter(string key, StepParameterViewModel parameter)
    {
        if (this.Any(p => p.Key == key))
        {
            throw new InvalidOperationException($"Key {key} already exists");
        }

        InsertSorted(new StepParameterRecord(key, parameter));
    }

    internal void RemoveAnswer() => RemoveParameter(QuestionParameterNames.Answer);

    internal bool RemoveParameter(string parameterName)
    {
        for (var i = 0; i < Count; i++)
        {
            if (this[i].Key == parameterName)
            {
                RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    internal bool TryGetValue(string key, [NotNullWhen(true)] out StepParameterViewModel? parameter)
    {
        foreach (var item in this)
        {
            if (item.Key == key)
            {
                parameter = item.Value;
                return true;
            }
        }

        parameter = null;
        return false;
    }
}

/// <summary>
/// Provides one canonical parameter entry plus non-persistent rename and conversion drafts for an editor.
/// </summary>
public sealed class StepParameterRecord : INotifyPropertyChanged
{
    private Func<string, bool>? _keyValidator;
    private string _draftKey;
    private string _draftKind;
    private IReadOnlyList<string> _availableKinds = Array.Empty<string>();

    /// <summary>Gets the canonical parameter key.</summary>
    public string Key { get; }

    /// <summary>Gets the canonical parameter value view model.</summary>
    public StepParameterViewModel Value { get; }

    /// <summary>Gets or sets the candidate key committed by the rename command.</summary>
    public string DraftKey
    {
        get => _draftKey;
        set
        {
            if (_draftKey == value)
            {
                return;
            }

            _draftKey = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DraftKey)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRename)));
        }
    }

    /// <summary>Gets or sets the candidate canonical kind committed by the conversion command.</summary>
    public string DraftKind
    {
        get => _draftKind;
        set
        {
            if (_draftKind == value)
            {
                return;
            }

            _draftKind = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DraftKind)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanConvert)));
        }
    }

    /// <summary>Gets known conversion targets plus the current opaque kind, when applicable.</summary>
    public IReadOnlyList<string> AvailableKinds => _availableKinds;

    /// <summary>Gets whether the current key draft is non-empty, unique, and different.</summary>
    public bool CanRename => _draftKey != Key && _keyValidator?.Invoke(_draftKey) == true;

    /// <summary>Gets whether the selected draft kind is a different supported conversion target.</summary>
    public bool CanConvert => _draftKind != GetCurrentKind()
        && KnownKind(_draftKind);

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Initializes an editor record over an existing canonical parameter.</summary>
    public StepParameterRecord(string key, StepParameterViewModel value)
    {
        Key = key;
        Value = value;
        _draftKey = key;
        _draftKind = GetCurrentKind();
    }

    internal void AttachValidation(Func<string, bool> keyValidator, IReadOnlyList<string> knownKinds)
    {
        _keyValidator = keyValidator;
        var currentKind = GetCurrentKind();
        _availableKinds = knownKinds.Contains(currentKind)
            ? knownKinds
            : new[] { currentKind }.Concat(knownKinds).ToArray();
        ResetDrafts();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailableKinds)));
    }

    internal void ResetDrafts()
    {
        DraftKey = Key;
        DraftKind = GetCurrentKind();
        RefreshValidation();
    }

    internal void RefreshValidation()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRename)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanConvert)));
    }

    private string GetCurrentKind() => Value.Model.IsRef
        ? StepParametersViewModel.ReferenceParameterKind
        : Value.Model.Type;

    private static bool KnownKind(string parameterKind) =>
        parameterKind == StepParameterTypes.Simple
        || parameterKind == StepParameterTypes.Content
        || parameterKind == StepParameterTypes.Group
        || parameterKind == StepParameterTypes.NumberSet
        || parameterKind == StepParametersViewModel.ReferenceParameterKind;
}
