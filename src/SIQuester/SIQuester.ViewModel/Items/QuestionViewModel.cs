using SIPackages;
using SIPackages.Core;
using SIQuester.Model;
using SIQuester.ViewModel.Helpers;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Defines a package question view model.
/// </summary>
public sealed class QuestionViewModel : ItemViewModel<Question>
{
    private const int DefaultAnswerDurationSeconds = 5;

    private ThemeViewModel? _ownerTheme;

    public ThemeViewModel? OwnerTheme
    {
        get => _ownerTheme;
        set
        {
            if (!ReferenceEquals(_ownerTheme, value))
            {
                _ownerTheme = value;
                UpdateStructuralCommands();
            }
        }
    }

    public override IItemViewModel? Owner => OwnerTheme;

    public AnswersViewModel Right { get; private set; }

    public AnswersViewModel Wrong { get; private set; }

    /// <summary>
    /// Gets or sets the first plain question text item without replacing other scenario content.
    /// </summary>
    public string QuestionText
    {
        get => GetPrimaryQuestionTextItem()?.Value ?? string.Empty;
        set
        {
            if (Model.Script != null)
            {
                SetScriptQuestionText(value);
                return;
            }

            var textItem = GetPrimaryQuestionTextItem();

            if (textItem == null)
            {
                using var change = OwnerTheme?.OwnerRound?.OwnerPackage?.Document?.OperationsManager.BeginComplexChange();
                var content = EnsureLegacyQuestionContent();
                content.Add(new ContentItemViewModel(new ContentItem
                {
                    Type = ContentTypes.Text,
                    Value = value,
                    Placement = ContentPlacements.Screen,
                }) { Owner = content });
                change?.Commit();
            }
            else if (textItem.Value != value)
            {
                textItem.Value = value;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the first right answer while preserving additional answers.
    /// </summary>
    public string PrimaryRightAnswer
    {
        get => Right.FirstOrDefault() ?? string.Empty;
        set => SetPrimaryAnswer(Right, value, nameof(PrimaryRightAnswer));
    }

    /// <summary>
    /// Gets or sets the first wrong answer while preserving additional answers.
    /// </summary>
    public string PrimaryWrongAnswer
    {
        get => Wrong.FirstOrDefault() ?? string.Empty;
        set => SetPrimaryAnswer(Wrong, value, nameof(PrimaryWrongAnswer));
    }

    public event Action<QuestionViewModel, string>? TypeNameChanged;

    public string TypeName
    {
        get => Model.TypeName;
        set
        {
            if (Model.TypeName == value)
            {
                return;
            }

            var oldValue = Model.TypeName;
            Model.TypeName = value;
            TypeNameChanged?.Invoke(this, oldValue);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSecretQuestion));
            OnPropertyChanged(nameof(UsesSecretTheme));
        }
    }

    public StepParametersViewModel Parameters { get; private set; }

    /// <summary>
    /// Gets legacy question content when the question does not use an explicit script.
    /// </summary>
    public ContentItemsViewModel? LegacyContent { get; private set; }

    /// <summary>
    /// Gets canonical script steps without converting legacy questions.
    /// </summary>
    public ScriptStepsViewModel ScriptSteps { get; private set; }

    public bool HasScript => Model.Script != null;

    public bool HasLegacyContent => LegacyContent != null;

    /// <summary>
    /// Gets whether the selected canonical question behavior uses secret-question parameters.
    /// </summary>
    public bool IsSecretQuestion => TypeName == QuestionTypes.Secret
        || TypeName == QuestionTypes.SecretPublicPrice
        || TypeName == QuestionTypes.SecretNoQuestion;

    /// <summary>
    /// Gets whether the selected secret behavior exposes a theme announced to players.
    /// </summary>
    public bool UsesSecretTheme => TypeName == QuestionTypes.Secret
        || TypeName == QuestionTypes.SecretPublicPrice;

    /// <summary>
    /// Gets the canonical secret-question theme parameter, when present.
    /// </summary>
    public StepParameterViewModel? SecretThemeParameter =>
        Parameters.TryGetValue(QuestionParameterNames.Theme, out var parameter) ? parameter : null;

    /// <summary>
    /// Gets the canonical secret-question price editor, when present.
    /// </summary>
    public NumberSetEditorNewViewModel? SecretPrice =>
        Parameters.TryGetValue(QuestionParameterNames.Price, out var parameter) ? parameter.NumberSetValue : null;

    /// <summary>
    /// Gets or sets whether a secret question may be given to the current player.
    /// </summary>
    public bool SecretAllowsCurrentPlayer
    {
        get => Parameters.TryGetValue(QuestionParameterNames.SelectionMode, out var parameter)
            && parameter.Model.SimpleValue == StepParameterValues.SetAnswererSelect_Any;
        set
        {
            var selectionMode = value
                ? StepParameterValues.SetAnswererSelect_Any
                : StepParameterValues.SetAnswererSelect_ExceptCurrent;

            if (!Parameters.TryGetValue(QuestionParameterNames.SelectionMode, out var parameter))
            {
                Parameters.InsertSorted(new StepParameterRecord(
                    QuestionParameterNames.SelectionMode,
                    new StepParameterViewModel(this, new StepParameter { SimpleValue = selectionMode })));
            }
            else if (parameter.Model.SimpleValue != selectionMode)
            {
                parameter.Model.SimpleValue = selectionMode;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets canonical content shown after the answer, when configured.
    /// </summary>
    public ContentItemsViewModel? PostAnswerContent =>
        Parameters.TryGetValue(QuestionParameterNames.Answer, out var parameter) ? parameter.ContentValue : null;

    public bool HasPostAnswerContent => PostAnswerContent != null;

    public ICommand AddComplexAnswer { get; private set; }

    public ICommand RemoveComplexAnswer { get; private set; }

    public SimpleCommand AddWrongAnswers { get; private set; }

    public ICommand ClearType { get; private set; }

    public override ICommand Add { get; protected set; }

    public override ICommand? Remove { get; protected set; }

    public ICommand Clone { get; private set; }

    /// <summary>Duplicates this question immediately after its current position.</summary>
    public SimpleCommand Duplicate { get; }

    /// <summary>Moves this question one position toward the start of its theme.</summary>
    public SimpleCommand MoveEarlier { get; }

    /// <summary>Moves this question one position toward the end of its theme.</summary>
    public SimpleCommand MoveLater { get; }

    public ICommand SetQuestionType { get; private set; }

    public ICommand SetAnswerType { get; private set; }

    public ICommand SwitchEmpty { get; private set; }

    public SimpleCommand SetAnswerTime { get; private set; }

    public SimpleCommand ClearAnswerTime { get; private set; }

    public bool HasAnswerDuration => AnswerDuration != null;

    /// <summary>
    /// Gets the answer duration in seconds if set, or null if not set.
    /// </summary>
    public int? AnswerDuration
    {
        get
        {
            if (Parameters.TryGetValue(QuestionParameterNames.AnswerDuration, out var durationParam)
                && int.TryParse(durationParam.Model.SimpleValue, out var duration)
                && duration > 0)
            {
                return duration;
            }

            return null;
        }
        set
        {
            // Remove parameter when value is null or 0 (like content item duration)
            if (value == null || value == 0)
            {
                Parameters.RemoveParameter(QuestionParameterNames.AnswerDuration);
                SetAnswerTime.CanBeExecuted = true;
                ClearAnswerTime.CanBeExecuted = false;
            }
            else
            {
                if (!Parameters.TryGetValue(QuestionParameterNames.AnswerDuration, out var durationParam))
                {
                    durationParam = new StepParameterViewModel(this, new StepParameter
                    {
                        Type = StepParameterTypes.Simple,
                        SimpleValue = value.Value.ToString()
                    });

                    Parameters.AddParameter(QuestionParameterNames.AnswerDuration, durationParam);
                }
                else
                {
                    durationParam.Model.SimpleValue = value.Value.ToString();
                }

                SetAnswerTime.CanBeExecuted = false;
                ClearAnswerTime.CanBeExecuted = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAnswerDuration));
        }
    }

    /// <summary>
    /// Tries to get question answer options.
    /// </summary>
    public StepParameterViewModel? AnswerOptions
    {
        get
        {
            if (Parameters == null)
            {
                return null;
            }

            Parameters.TryGetValue(QuestionParameterNames.AnswerOptions, out var answerOptionsParameter);
            return answerOptionsParameter;
        }
    }

    /// <summary>
    /// Gets the numeric answer view model if the question uses numeric answers.
    /// </summary>
    public NumericAnswerViewModel? NumericAnswer
    {
        get
        {
            if (Parameters.TryGetValue(QuestionParameterNames.AnswerType, out var answerTypeParameter)
                && answerTypeParameter.Model.SimpleValue == StepParameterValues.SetAnswerTypeType_Number)
            {
                return _numericAnswer ??= new NumericAnswerViewModel(this);
            }

            _numericAnswer = null;
            return null;
        }
    }

    /// <summary>
    /// Gets the point answer view model if the question uses point answers.
    /// </summary>
    public PointAnswerViewModel? PointAnswer
    {
        get
        {
            if (Parameters.TryGetValue(QuestionParameterNames.AnswerType, out var answerTypeParameter)
                && answerTypeParameter.Model.SimpleValue == StepParameterValues.SetAnswerTypeType_Point)
            {
                 return _pointAnswer ??= new PointAnswerViewModel(this);
            }

            if (_pointAnswer != null)
            {
                _pointAnswer.Dispose();
                _pointAnswer = null;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets whether this question uses numeric answers.
    /// </summary>
    public bool IsNumericAnswer
    {
        get
        {
            return Parameters.TryGetValue(QuestionParameterNames.AnswerType, out var answerTypeParameter)
                && answerTypeParameter.Model.SimpleValue == StepParameterValues.SetAnswerTypeType_Number;
        }
    }

    /// <summary>
    /// Gets whether this question uses point answers.
    /// </summary>
    public bool IsPointAnswer
    {
        get
        {
            return Parameters.TryGetValue(QuestionParameterNames.AnswerType, out var answerTypeParameter)
                && answerTypeParameter.Model.SimpleValue == StepParameterValues.SetAnswerTypeType_Point;
        }
    }

    /// <summary>
    /// Gets whether this question uses selectable answer options.
    /// </summary>
    public bool IsSelectAnswer =>
        Parameters.TryGetValue(QuestionParameterNames.AnswerType, out var answerTypeParameter)
        && answerTypeParameter.Model.SimpleValue == StepParameterValues.SetAnswerTypeType_Select;

    /// <summary>
    /// Gets whether this question answer validation is managed by client.
    /// </summary>
    public bool IsManagedByClient
    {
        get
        {
            return Parameters.TryGetValue(QuestionParameterNames.AnswerType, out var answerTypeParameter)
                && answerTypeParameter.Model.SimpleValue == StepParameterValues.SetAnswerTypeType_ManagedByClient;
        }
    }

    /// <summary>
    /// Gets whether right and wrong answers are represented by plain text collections.
    /// </summary>
    public bool UsesSimpleAnswerCollections =>
        Parameters.AnswerType == StepParameterValues.SetAnswerTypeType_Text;

    private int? _triesPercent;

    /// <summary>
    /// Gets the percentage of times the question was answered (answered / shown * 100).
    /// Returns null if statistics are not available.
    /// </summary>
    public int? TriesPercent
    {
        get => _triesPercent;
        set
        {
            if (_triesPercent != value)
            {
                _triesPercent = value;
                OnPropertyChanged();
            }
        }
    }
    
    private int? _rightPercent;
    private NumericAnswerViewModel? _numericAnswer;
    private PointAnswerViewModel? _pointAnswer;
    private StepParameterViewModel? _observedAnswerTypeParameter;

    /// <summary>
    /// Gets the percentage of correct answers (correct / (correct + wrong) * 100).
    /// Returns null if statistics are not available.
    /// </summary>
    public int? RightPercent
    {
        get => _rightPercent;
        set
        {
            if (_rightPercent != value)
            {
                _rightPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public QuestionViewModel(Question question)
        : base(question)
    {
        Right = new AnswersViewModel(this, question.Right, true);
        Wrong = new AnswersViewModel(this, question.Wrong, false);
        Parameters = new StepParametersViewModel(this, question.Parameters);
        ScriptSteps = new ScriptStepsViewModel(this, question.Script);

        if (question.Script == null
            && Parameters.TryGetValue(QuestionParameterNames.Question, out var questionParameter))
        {
            LegacyContent = questionParameter.ContentValue;
        }

        BindHelper.Bind(Right, question.Right);
        BindHelper.Bind(Wrong, question.Wrong);

        Add = new SimpleCommand(Add_Executed);

        AddComplexAnswer = new SimpleCommand(AddComplexAnswer_Executed);
        RemoveComplexAnswer = new SimpleCommand(RemoveComplexAnswer_Executed);
        AddWrongAnswers = new SimpleCommand(AddWrongAnswers_Executed);

        ClearType = new SimpleCommand(ClearType_Executed);

        Clone = new SimpleCommand(CloneQuestion_Executed);
        Duplicate = new SimpleCommand(DuplicateQuestion_Executed);
        MoveEarlier = new SimpleCommand(_ => Move(-1));
        MoveLater = new SimpleCommand(_ => Move(1));
        Remove = new SimpleCommand(RemoveQuestion_Executed);

        SetQuestionType = new SimpleCommand(SetQuestionType_Executed);
        SetAnswerType = new SimpleCommand(SetAnswerType_Executed);
        SwitchEmpty = new SimpleCommand(SwitchEmpty_Executed);
        SetAnswerTime = new SimpleCommand(SetAnswerTime_Executed) { CanBeExecuted = AnswerDuration == null };
        ClearAnswerTime = new SimpleCommand(_ => AnswerDuration = null) { CanBeExecuted = AnswerDuration != null };

        Right.CollectionChanged += Right_CollectionChanged;
        Wrong.CollectionChanged += Wrong_CollectionChanged;
        Parameters.CollectionChanged += Parameters_AnswerTypeCollectionChanged;
        RefreshParameterSubscriptions();
        UpdateStructuralCommands();
    }

    private ContentItem? GetPrimaryQuestionTextItem()
    {
        if (Model.Script != null)
        {
            foreach (var step in Model.Script.Steps)
            {
                if (step.Type == StepTypes.AskAnswer)
                {
                    break;
                }

                if (step.Type == StepTypes.ShowContent
                    && step.Parameters.TryGetValue(StepParameterNames.Content, out var content)
                    && content.ContentValue != null)
                {
                    var textItem = content.ContentValue.FirstOrDefault(item => item.Type == ContentTypes.Text);

                    if (textItem != null)
                    {
                        return textItem;
                    }
                }
            }

            return null;
        }

        if (!Parameters.TryGetValue(QuestionParameterNames.Question, out var questionParameter)
            || questionParameter.ContentValue == null)
        {
            return null;
        }

        return questionParameter.ContentValue
            .Select(item => item.Model)
            .FirstOrDefault(item => item.Type == ContentTypes.Text);
    }

    private ContentItemsViewModel EnsureLegacyQuestionContent()
    {
        if (!Parameters.TryGetValue(QuestionParameterNames.Question, out var questionParameter)
            || questionParameter.ContentValue == null)
        {
            questionParameter = new StepParameterViewModel(this, new StepParameter
            {
                Type = StepParameterTypes.Content,
                ContentValue = new List<ContentItem>(),
            });
            Parameters.AddParameter(QuestionParameterNames.Question, questionParameter);
        }

        return questionParameter.ContentValue!;
    }

    /// <summary>
    /// Gets the primary editable question content, creating a canonical container without changing
    /// whether the question uses legacy parameters or an explicit script.
    /// </summary>
    internal ContentItemsViewModel GetOrCreatePrimaryContent()
    {
        if (Model.Script == null)
        {
            return EnsureLegacyQuestionContent();
        }

        ScriptStepViewModel? firstShowContentStep = null;
        var insertionIndex = ScriptSteps.Count;

        for (var i = 0; i < ScriptSteps.Count; i++)
        {
            var step = ScriptSteps[i];

            if (step.Model.Type == StepTypes.AskAnswer)
            {
                insertionIndex = i;
                break;
            }

            if (step.Model.Type != StepTypes.ShowContent)
            {
                continue;
            }

            firstShowContentStep ??= step;

            if (step.Parameters.TryGetValue(StepParameterNames.Content, out var parameter)
                && parameter.ContentValue != null)
            {
                return parameter.ContentValue;
            }
        }

        if (firstShowContentStep == null)
        {
            var step = new Step { Type = StepTypes.ShowContent };
            step.Parameters[StepParameterNames.Content] = CreateContentParameter();
            var stepViewModel = new ScriptStepViewModel(ScriptSteps, this, step);
            ScriptSteps.Insert(insertionIndex, stepViewModel);
            return stepViewModel.Parameters.Single().Value.ContentValue!;
        }

        var contentParameter = new StepParameterViewModel(this, CreateContentParameter(), isTopLevel: true);
        firstShowContentStep.Parameters.AddParameter(StepParameterNames.Content, contentParameter);
        return contentParameter.ContentValue!;
    }

    private static StepParameter CreateContentParameter() => new()
    {
        Type = StepParameterTypes.Content,
        ContentValue = new List<ContentItem>(),
    };

    private void SetScriptQuestionText(string value)
    {
        var textItem = GetPrimaryQuestionTextItem();

        if (textItem != null)
        {
            if (textItem.Value == value)
            {
                return;
            }

            var oldValue = textItem.Value;
            textItem.Value = value;
            RecordQuestionTextChange(new QuestionTextValueChange(this, textItem, oldValue));
            OnPropertyChanged(nameof(QuestionText));
            return;
        }

        var script = Model.Script!;
        var step = script.Steps.FirstOrDefault(item => item.Type == StepTypes.ShowContent);
        var stepCreated = step == null;
        var stepIndex = stepCreated ? 0 : script.Steps.IndexOf(step!);
        step ??= new Step { Type = StepTypes.ShowContent };

        if (stepCreated)
        {
            script.Steps.Insert(stepIndex, step);
        }

        step.Parameters.TryGetValue(StepParameterNames.Content, out var oldParameter);
        var contentParameter = oldParameter;
        var parameterReplaced = contentParameter?.ContentValue == null;

        if (parameterReplaced)
        {
            contentParameter = new StepParameter
            {
                Type = StepParameterTypes.Content,
                ContentValue = new List<ContentItem>(),
            };
            step.Parameters[StepParameterNames.Content] = contentParameter;
        }

        var content = contentParameter!.ContentValue!;
        var addedItem = new ContentItem
        {
            Type = ContentTypes.Text,
            Value = value,
            Placement = ContentPlacements.Screen,
        };
        var itemIndex = content.Count;
        content.Add(addedItem);

        RecordQuestionTextChange(new QuestionTextAdditionChange(
            this,
            script,
            step,
            stepIndex,
            stepCreated,
            oldParameter,
            parameterReplaced ? contentParameter : null,
            content,
            addedItem,
            itemIndex));
        OnPropertyChanged(nameof(QuestionText));
    }

    private void RecordQuestionTextChange(IChange change) =>
        OwnerTheme?.OwnerRound?.OwnerPackage?.Document?.OperationsManager.AddChange(change);

    internal void NotifyQuestionTextChanged() => OnPropertyChanged(nameof(QuestionText));

    private sealed class QuestionTextValueChange(
        QuestionViewModel owner,
        ContentItem item,
        string value) : IChange
    {
        private string _value = value;

        public void Undo()
        {
            (_value, item.Value) = (item.Value, _value);
            owner.NotifyQuestionTextChanged();
        }

        public void Redo() => Undo();
    }

    private sealed class QuestionTextAdditionChange(
        QuestionViewModel owner,
        Script script,
        Step step,
        int stepIndex,
        bool stepCreated,
        StepParameter? oldParameter,
        StepParameter? newParameter,
        IList<ContentItem> content,
        ContentItem item,
        int itemIndex) : IChange
    {
        public void Undo()
        {
            content.Remove(item);

            if (newParameter != null)
            {
                if (oldParameter == null)
                {
                    step.Parameters.Remove(StepParameterNames.Content);
                }
                else
                {
                    step.Parameters[StepParameterNames.Content] = oldParameter;
                }
            }

            if (stepCreated)
            {
                script.Steps.Remove(step);
            }

            owner.NotifyQuestionTextChanged();
        }

        public void Redo()
        {
            if (stepCreated && !script.Steps.Contains(step))
            {
                script.Steps.Insert(Math.Min(stepIndex, script.Steps.Count), step);
            }

            if (newParameter != null)
            {
                step.Parameters[StepParameterNames.Content] = newParameter;
            }

            content.Insert(Math.Min(itemIndex, content.Count), item);
            owner.NotifyQuestionTextChanged();
        }
    }

    private void SetPrimaryAnswer(AnswersViewModel answers, string value, string propertyName)
    {
        if (answers.Count == 0)
        {
            answers.Add(value);
        }
        else if (answers[0] != value)
        {
            answers[0] = value;
        }

        OnPropertyChanged(propertyName);
    }

    private void Right_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(PrimaryRightAnswer));

    private void Add_Executed(object? arg)
    {
        if (Parameters.TryGetValue(QuestionParameterNames.Question, out var questionParameter))
        {
            questionParameter.ContentValue?.AddText.Execute(arg);
        }
    }

    private void ClearType_Executed(object? arg) => TypeName = QuestionTypes.Default;

    private void SetAnswerTime_Executed(object? arg)
    {
        AnswerDuration ??= DefaultAnswerDurationSeconds;
    }

    private void Wrong_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AddWrongAnswers.CanBeExecuted = Model.Wrong.Count == 0;
        OnPropertyChanged(nameof(PrimaryWrongAnswer));
    }

    private void AddComplexAnswer_Executed(object? arg)
    {
        try
        {
            Parameters.AddAnswer(new StepParameterViewModel(this, new StepParameter
            {
                Type = StepParameterTypes.Content,
                ContentValue = new List<ContentItem>(new[]
                {
                    new ContentItem { Type = ContentTypes.Text, Placement = ContentPlacements.Screen, Value = "" }
                })
            }));
        }
        catch (Exception exc)
        {
            PlatformSpecific.PlatformManager.Instance.Inform(exc.Message, true);
        }
    }

    private void RemoveComplexAnswer_Executed(object? arg) => Parameters.RemoveAnswer();

    private void AddWrongAnswers_Executed(object? arg)
    {
        if (IsManagedByClient)
        {
            return;
        }

        QDocument.ActivatedObject = Wrong;

        try
        {
            Wrong.Add("");
        }
        catch (Exception ex)
        {
            PlatformSpecific.PlatformManager.Instance.Inform(ex.Message, true);
        }
    }

    private void CloneQuestion_Executed(object? arg)
    {
        if (OwnerTheme == null)
        {
            return;
        }

        var ownerPackage = OwnerTheme.OwnerRound?.OwnerPackage;

        if (ownerPackage == null)
        {
            return;
        }

        var quest = Model.Clone();
        var newQuestionViewModel = new QuestionViewModel(quest);
        OwnerTheme.Questions.Add(newQuestionViewModel);
        ownerPackage.Document.Navigate.Execute(newQuestionViewModel);
    }

    private void DuplicateQuestion_Executed(object? arg)
    {
        var document = OwnerTheme?.OwnerRound?.OwnerPackage?.Document;

        if (document == null || OwnerTheme?.Questions.Contains(this) != true)
        {
            return;
        }

        document.FlatQuestions.DuplicateAfter(this, document.Settings.ChangePriceOnMove);
    }

    private void Move(int offset)
    {
        var document = OwnerTheme?.OwnerRound?.OwnerPackage?.Document;

        if (document == null || OwnerTheme?.Questions.Contains(this) != true)
        {
            return;
        }

        document.FlatQuestions.MoveWithinTheme(this, offset, document.Settings.ChangePriceOnMove);
    }

    internal void UpdateStructuralCommands()
    {
        var ownerTheme = OwnerTheme;
        var index = ownerTheme?.Questions.IndexOf(this) ?? -1;
        MoveEarlier.CanBeExecuted = index > 0;
        MoveLater.CanBeExecuted = ownerTheme != null && index >= 0 && index + 1 < ownerTheme.Questions.Count;
        Duplicate.CanBeExecuted = index >= 0;
    }

    private void RemoveQuestion_Executed(object? arg)
    {
        var ownerTheme = OwnerTheme;

        if (ownerTheme == null)
        {
            return;
        }

        var ownerDocument = ownerTheme.OwnerRound?.OwnerPackage?.Document;

        if (ownerDocument == null)
        {
            return;
        }

        try
        {
            var index = ownerTheme.Questions.IndexOf(this);
            var isActive = ownerDocument.ActiveNode == this;

            using var change = ownerDocument.OperationsManager.BeginComplexChange();
            ownerTheme.Questions.Remove(this);
            change.Commit();

            if (isActive)
            {
                ownerDocument.Navigate.Execute(index < ownerTheme.Questions.Count ? ownerTheme.Questions[index] : ownerTheme);
            }
        }
        catch (Exception exc)
        {
            PlatformSpecific.PlatformManager.Instance.Inform(exc.Message, true);
        }
    }

    private void SetQuestionType_Executed(object? arg)
    {
        var typeName = (string?)arg ?? "";
        TypeName = typeName == QuestionTypes.Default ? "?" : typeName;
    }

    private void SetAnswerType_Executed(object? arg)
    {
        try
        {
            var document = OwnerTheme?.OwnerRound?.OwnerPackage?.Document ?? throw new InvalidOperationException("document is undefined");
            
            if (Parameters == null || arg == null)
            {
                return;
            }

            var answerType = (string)arg;

            // Reapplying a valid select type must not recreate options or discard user edits.
            if (answerType == StepParameterValues.SetAnswerTypeType_Select
                && Parameters.AnswerType == answerType
                && AnswerOptions?.GroupValue != null)
            {
                OnAnswerTypeChanged();
                return;
            }

            if (answerType == StepParameterValues.SetAnswerTypeType_Text)
            {
                // Default value; remove parameter
                using var innerChange = document.OperationsManager.BeginComplexChange();

                // Take right answer from options and put it to the right answer field
                if (Right.Count > 0
                    && Parameters.TryGetValue(QuestionParameterNames.AnswerOptions, out var answerOptions)
                    && answerOptions.GroupValue != null
                    && answerOptions.GroupValue.TryGetValue(Right[0], out var rightAnswer)
                    && rightAnswer.ContentValue != null
                    && rightAnswer.ContentValue.Count > 0
                    && rightAnswer.ContentValue[0].Model.Type == ContentTypes.Text)
                {
                    Right[0] = rightAnswer.ContentValue[0].Model.Value;
                }

                Parameters.RemoveParameter(QuestionParameterNames.AnswerType);
                Parameters.RemoveParameter(QuestionParameterNames.AnswerOptions);
                Parameters.RemoveParameter(QuestionParameterNames.AnswerDeviation);

                if (Right.Count == 0)
                {
                    Right.Add("");
                }

                innerChange.Commit();
                OnAnswerTypeChanged();
                return;
            }

            using var change = document.OperationsManager.BeginComplexChange();

            if (!Parameters.TryGetValue(QuestionParameterNames.AnswerType, out var answerTypeParameter))
            {
                answerTypeParameter = new StepParameterViewModel(this, new StepParameter
                {
                    Type = StepParameterTypes.Simple,
                    SimpleValue = answerType
                });

                Parameters.AddParameter(QuestionParameterNames.AnswerType, answerTypeParameter);
            }
            else
            {
                answerTypeParameter.Model.SimpleValue = answerType;
            }

            if (answerType == StepParameterValues.SetAnswerTypeType_Number)
            {
                // Add numeric deviation parameter with default value 0
                if (!Parameters.TryGetValue(QuestionParameterNames.AnswerDeviation, out var deviationParameter))
                {
                    deviationParameter = new StepParameterViewModel(this, new StepParameter
                    {
                        Type = StepParameterTypes.Simple,
                        SimpleValue = "0"
                    });

                    Parameters.AddParameter(QuestionParameterNames.AnswerDeviation, deviationParameter);
                }

                // Remove answer options if they exist
                Parameters.RemoveParameter(QuestionParameterNames.AnswerOptions);
            }
            else if (answerType == StepParameterValues.SetAnswerTypeType_Point)
            {
                // Add default point deviation parameter
                if (!Parameters.TryGetValue(QuestionParameterNames.AnswerDeviation, out var deviationParameter))
                {
                    deviationParameter = new StepParameterViewModel(this, new StepParameter
                    {
                        Type = StepParameterTypes.Simple,
                        SimpleValue = "0"
                    });

                    Parameters.AddParameter(QuestionParameterNames.AnswerDeviation, deviationParameter);
                }

                // Remove answer options if they exist
                Parameters.RemoveParameter(QuestionParameterNames.AnswerOptions);
            }
            else if (answerType == StepParameterValues.SetAnswerTypeType_Select)
            {
                // Replace a stale or malformed options parameter while preserving valid options above.
                Parameters.RemoveParameter(QuestionParameterNames.AnswerOptions);

                var options = new StepParameter
                {
                    Type = StepParameterTypes.Group,
                    GroupValue = new StepParameters()
                };

                static StepParameter answerOptionGenerator(string initialValue) => new()
                {
                    Type = StepParameterTypes.Content,
                    ContentValue = new List<ContentItem>
                    {
                        new() { Type = ContentTypes.Text, Value = initialValue },
                    }
                };

                var rightAnswer = Right.FirstOrDefault();

                for (var i = 0; i < AppSettings.Default.SelectOptionCount; i++)
                {
                    var option = answerOptionGenerator(i == 0 && rightAnswer != null ? rightAnswer : "");
                    options.GroupValue.Add(IndexLabelHelper.GetIndexLabel(i), option);
                }

                var optionsViewModel = new StepParameterViewModel(this, options);

                Parameters.AddParameter(QuestionParameterNames.AnswerOptions, optionsViewModel);
                Right.ClearOneByOne();
                Right.Add(IndexLabelHelper.GetIndexLabel(0));
                Wrong.ClearOneByOne();

                // Remove deviation parameter for select type
                Parameters.RemoveParameter(QuestionParameterNames.AnswerDeviation);
            }
            else if (answerType == StepParameterValues.SetAnswerTypeType_ManagedByClient)
            {
                Right.ClearOneByOne();
                Wrong.ClearOneByOne();
                Parameters.RemoveParameter(QuestionParameterNames.AnswerOptions);
                Parameters.RemoveParameter(QuestionParameterNames.AnswerDeviation);
            }
            else
            {
                // For other answer types, remove both options and deviation
                Parameters.RemoveParameter(QuestionParameterNames.AnswerOptions);
                Parameters.RemoveParameter(QuestionParameterNames.AnswerDeviation);
            }

            change.Commit();
            OnAnswerTypeChanged();
        }
        catch (Exception exc)
        {
            PlatformSpecific.PlatformManager.Instance.Inform(exc.Message, true);
        }
    }

    private void OnAnswerTypeChanged()
    {
        if (!IsNumericAnswer)
        {
            _numericAnswer = null;
        }

        if (!IsPointAnswer && _pointAnswer != null)
        {
            _pointAnswer.Dispose();
            _pointAnswer = null;
        }

        OnPropertyChanged(nameof(IsNumericAnswer));
        OnPropertyChanged(nameof(NumericAnswer));
        OnPropertyChanged(nameof(IsPointAnswer));
        OnPropertyChanged(nameof(PointAnswer));
        OnPropertyChanged(nameof(IsSelectAnswer));
        OnPropertyChanged(nameof(AnswerOptions));
        OnPropertyChanged(nameof(IsManagedByClient));
        OnPropertyChanged(nameof(UsesSimpleAnswerCollections));
    }

    private void Parameters_AnswerTypeCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshLegacyContent();

        OnPropertyChanged(nameof(SecretThemeParameter));
        OnPropertyChanged(nameof(SecretPrice));
        OnPropertyChanged(nameof(SecretAllowsCurrentPlayer));
        OnPropertyChanged(nameof(PostAnswerContent));
        OnPropertyChanged(nameof(HasPostAnswerContent));
        OnPropertyChanged(nameof(AnswerDuration));
        OnPropertyChanged(nameof(HasAnswerDuration));

        if (RefreshParameterSubscriptions())
        {
            OnAnswerTypeChanged();
        }
    }

    private void RefreshLegacyContent()
    {
        if (Model.Script != null)
        {
            return;
        }

        var content = Parameters.TryGetValue(QuestionParameterNames.Question, out var questionParameter)
            ? questionParameter.ContentValue
            : null;

        if (ReferenceEquals(LegacyContent, content))
        {
            return;
        }

        LegacyContent = content;
        OnPropertyChanged(nameof(LegacyContent));
        OnPropertyChanged(nameof(HasLegacyContent));
    }

    private StepParameterViewModel? _observedSelectionModeParameter;

    private StepParameterViewModel? _observedAnswerDurationParameter;

    private bool RefreshParameterSubscriptions()
    {
        Parameters.TryGetValue(QuestionParameterNames.AnswerType, out var answerTypeParameter);

        var answerTypeChanged = !ReferenceEquals(_observedAnswerTypeParameter, answerTypeParameter);

        if (answerTypeChanged)
        {
            if (_observedAnswerTypeParameter != null)
            {
                _observedAnswerTypeParameter.Model.PropertyChanged -= AnswerTypeParameter_PropertyChanged;
            }

            _observedAnswerTypeParameter = answerTypeParameter;

            if (_observedAnswerTypeParameter != null)
            {
                _observedAnswerTypeParameter.Model.PropertyChanged += AnswerTypeParameter_PropertyChanged;
            }
        }

        RefreshParameterSubscription(
            QuestionParameterNames.SelectionMode,
            ref _observedSelectionModeParameter,
            SelectionModeParameter_PropertyChanged);

        RefreshParameterSubscription(
            QuestionParameterNames.AnswerDuration,
            ref _observedAnswerDurationParameter,
            AnswerDurationParameter_PropertyChanged);

        return answerTypeChanged;
    }

    private void RefreshParameterSubscription(
        string parameterName,
        ref StepParameterViewModel? observedParameter,
        PropertyChangedEventHandler handler)
    {
        Parameters.TryGetValue(parameterName, out var parameter);

        if (ReferenceEquals(observedParameter, parameter))
        {
            return;
        }

        if (observedParameter != null)
        {
            observedParameter.Model.PropertyChanged -= handler;
        }

        observedParameter = parameter;

        if (observedParameter != null)
        {
            observedParameter.Model.PropertyChanged += handler;
        }
    }

    private void AnswerTypeParameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StepParameter.SimpleValue))
        {
            OnAnswerTypeChanged();
        }
    }

    private void SelectionModeParameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StepParameter.SimpleValue))
        {
            OnPropertyChanged(nameof(SecretAllowsCurrentPlayer));
        }
    }

    private void AnswerDurationParameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StepParameter.SimpleValue))
        {
            OnPropertyChanged(nameof(AnswerDuration));
            OnPropertyChanged(nameof(HasAnswerDuration));
            SetAnswerTime.CanBeExecuted = AnswerDuration == null;
            ClearAnswerTime.CanBeExecuted = AnswerDuration != null;
        }
    }

    private void SwitchEmpty_Executed(object? arg)
    {
        try
        {
            var document = OwnerTheme?.OwnerRound?.OwnerPackage?.Document ?? throw new InvalidOperationException("document is undefined");

            using var change = document.OperationsManager.BeginComplexChange();

            if (Model.Price == Question.InvalidPrice)
            {
                Model.Price = AppSettings.Default.QuestionBase * ((OwnerTheme?.Questions.IndexOf(this) ?? 0) + 1);

                Parameters!.ClearOneByOne();

                Parameters.AddParameter(QuestionParameterNames.Question, new StepParameterViewModel(this, new StepParameter
                {
                    Type = StepParameterTypes.Content,
                    ContentValue = new List<ContentItem>
                        {
                            new() { Type = ContentTypes.Text, Value = "" },
                        }
                }));

                Right.ClearOneByOne();
                Right.Add("");
                Wrong.ClearOneByOne();

                change.Commit();
                return;
            }

            Model.Price = Question.InvalidPrice;

            Parameters?.ClearOneByOne();
            TypeName = QuestionTypes.Default;

            Right.ClearOneByOne();
            Wrong.ClearOneByOne();

            change.Commit();
        }
        catch (Exception exc)
        {
            PlatformSpecific.PlatformManager.Instance.Inform(exc.Message, true);
        }
    }

    internal IEnumerable<ContentItemViewModel> GetContent() => GetContentFromParameters(Parameters);

    private static IEnumerable<ContentItemViewModel> GetContentFromParameters(StepParametersViewModel parameters)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.Value.ContentValue == null)
            {
                if (parameter.Value.GroupValue != null)
                {
                    foreach (var contentItem in GetContentFromParameters(parameter.Value.GroupValue))
                    {
                        yield return contentItem;
                    }
                }

                continue;
            }

            foreach (var contentItem in parameter.Value.ContentValue)
            {
                yield return contentItem;
            }
        }
    }

    public bool TryAddRightAnswerFromFileName(string fileName)
    {
        if (Parameters.TryGetValue(QuestionParameterNames.AnswerType, out var answerTypeParameter)
            && (answerTypeParameter.Model.SimpleValue == StepParameterValues.SetAnswerTypeType_Select
                || answerTypeParameter.Model.SimpleValue == StepParameterValues.SetAnswerTypeType_ManagedByClient))
        {
            return false;
        }

        if (Right.Last().Length == 0)
        {
            Right.RemoveAt(Right.Count - 1);
        }

        Right.Add(Path.GetFileNameWithoutExtension(fileName));
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Right.CollectionChanged -= Right_CollectionChanged;
            Wrong.CollectionChanged -= Wrong_CollectionChanged;
            Parameters.CollectionChanged -= Parameters_AnswerTypeCollectionChanged;

            if (_observedAnswerTypeParameter != null)
            {
                _observedAnswerTypeParameter.Model.PropertyChanged -= AnswerTypeParameter_PropertyChanged;
                _observedAnswerTypeParameter = null;
            }

            if (_observedSelectionModeParameter != null)
            {
                _observedSelectionModeParameter.Model.PropertyChanged -= SelectionModeParameter_PropertyChanged;
                _observedSelectionModeParameter = null;
            }

            if (_observedAnswerDurationParameter != null)
            {
                _observedAnswerDurationParameter.Model.PropertyChanged -= AnswerDurationParameter_PropertyChanged;
                _observedAnswerDurationParameter = null;
            }

            _pointAnswer?.Dispose();
            _pointAnswer = null;
            _numericAnswer = null;
        }

        base.Dispose(disposing);
    }
}
