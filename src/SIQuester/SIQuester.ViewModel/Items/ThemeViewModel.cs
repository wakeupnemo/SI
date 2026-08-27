using SIPackages;
using SIPackages.Core;
using SIQuester.Model;
using SIQuester.ViewModel.Helpers;
using SIQuester.ViewModel.PlatformSpecific;
using SIQuester.ViewModel.Properties;
using SIQuester.ViewModel.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Defines a package theme view model.
/// </summary>
public sealed class ThemeViewModel : ItemViewModel<Theme>
{
    /// <summary>
    /// Owner round view model.
    /// </summary>
    private RoundViewModel? _ownerRound;

    public RoundViewModel? OwnerRound
    {
        get => _ownerRound;
        set
        {
            if (!ReferenceEquals(_ownerRound, value))
            {
                _ownerRound = value;
                UpdateStructuralCommands();
            }
        }
    }

    public override IItemViewModel? Owner => OwnerRound;

    /// <summary>
    /// Theme questions.
    /// </summary>
    public ObservableCollection<QuestionViewModel> Questions { get; } = new();

    public override ICommand Add { get; protected set; }

    public override string AddHeader => Resources.AddQuestion;

    public override ICommand? Remove { get; protected set; }

    public ICommand Clone { get; }

    public SimpleCommand Duplicate { get; }

    public SimpleCommand MoveEarlier { get; }

    public SimpleCommand MoveLater { get; }

    /// <summary>
    /// Adds new question.
    /// </summary>
    public ICommand AddQuestion { get; private set; }

    /// <summary>
    /// Adds new question having no data.
    /// </summary>
    public ICommand AddEmptyQuestion { get; private set; }

    /// <summary>
    /// Generates questions with the help of GPT.
    /// </summary>
    public ICommand GenerateQuestions { get; private set; }

    public ICommand SortQuestions { get; private set; }

    /// <summary>
    /// Shuffles questions and prices.
    /// </summary>
    public ICommand ShuffleQuestions { get; private set; }

    public ThemeViewModel(Theme theme)
        : base(theme)
    {
        foreach (var question in theme.Questions)
        {
            Questions.Add(new QuestionViewModel(question) { OwnerTheme = this });
        }

        Questions.CollectionChanged += Questions_CollectionChanged;

        Clone = new SimpleCommand(CloneTheme_Executed);
        Duplicate = new SimpleCommand(DuplicateTheme_Executed);
        MoveEarlier = new SimpleCommand(_ => Move(-1));
        MoveLater = new SimpleCommand(_ => Move(1));
        Remove = new SimpleCommand(RemoveTheme_Executed);
        Add = AddQuestion = new SimpleCommand(AddQuestion_Executed);
        AddEmptyQuestion = new SimpleCommand(AddEmptyQuestion_Executed);
        GenerateQuestions = new SimpleCommand(GenerateQuestions_Executed);
        SortQuestions = new SimpleCommand(SortQuestions_Executed);
        ShuffleQuestions = new SimpleCommand(ShuffleQuestions_Executed);
        UpdateStructuralCommands();
    }

    private async void GenerateQuestions_Executed(object? arg)
    {
        try
        {
            await QuestionsGenerator.GenerateThemeQuestionsAsync(this);
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.ShowErrorMessage(exc.Message);
        }
    }

    private void Questions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems == null)
                {
                    break;
                }

                for (int i = e.NewStartingIndex; i < e.NewStartingIndex + e.NewItems.Count; i++)
                {
                    if (Questions[i].OwnerTheme != null)
                    {
                        throw new Exception(Resources.ErrorInsertingBindedQuestion);
                    }

                    Questions[i].OwnerTheme = this;
                    Model.Questions.Insert(i, Questions[i].Model);
                }
                break;

            case NotifyCollectionChangedAction.Replace:
                if (e.NewItems == null)
                {
                    break;
                }

                for (int i = e.NewStartingIndex; i < e.NewStartingIndex + e.NewItems.Count; i++)
                {
                    if (Questions[i].OwnerTheme != null && Questions[i].OwnerTheme != this)
                    {
                        throw new Exception(Resources.ErrorInsertingBindedQuestion);
                    }

                    Questions[i].OwnerTheme = this;
                    Model.Questions[i] = Questions[i].Model;
                }
                break;

            case NotifyCollectionChangedAction.Move:
                var temp = Model.Questions[e.OldStartingIndex];
                Model.Questions.RemoveAt(e.OldStartingIndex);
                Model.Questions.Insert(e.NewStartingIndex, temp);
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems == null)
                {
                    break;
                }

                foreach (QuestionViewModel question in e.OldItems)
                {
                    question.OwnerTheme = null;
                    Model.Questions.RemoveAt(e.OldStartingIndex);

                    OwnerRound?.OwnerPackage?.Document?.ClearLinks(question);
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                Model.Questions.Clear();

                foreach (var question in Questions)
                {
                    question.OwnerTheme = this;
                    Model.Questions.Add(question.Model);
                }
                break;
        }
    }

    private void CloneTheme_Executed(object? arg)
    {
        if (OwnerRound == null || OwnerRound.OwnerPackage == null)
        {
            return;
        }

        var newThemeViewModel = new ThemeViewModel(Model.Clone());
        OwnerRound.Themes.Add(newThemeViewModel);
        OwnerRound.OwnerPackage.Document.Navigate.Execute(newThemeViewModel);
    }

    private void DuplicateTheme_Executed(object? arg)
    {
        if (OwnerRound == null || OwnerRound.OwnerPackage == null)
        {
            return;
        }

        var newThemeViewModel = new ThemeViewModel(Model.Clone());
        var index = OwnerRound.Themes.IndexOf(this);

        if (index < 0)
        {
            return;
        }

        using var change = OwnerRound.OwnerPackage.Document.OperationsManager.BeginComplexChange();
        OwnerRound.Themes.Insert(index + 1, newThemeViewModel);
        change.Commit();
        OwnerRound.OwnerPackage.Document.Navigate.Execute(newThemeViewModel);
    }

    private void Move(int offset)
    {
        var ownerDocument = OwnerRound?.OwnerPackage?.Document;

        if (OwnerRound == null || ownerDocument == null)
        {
            return;
        }

        var sourceIndex = OwnerRound.Themes.IndexOf(this);
        var targetIndex = sourceIndex + offset;

        if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= OwnerRound.Themes.Count)
        {
            return;
        }

        using var change = ownerDocument.OperationsManager.BeginComplexChange();
        OwnerRound.Themes.Move(sourceIndex, targetIndex);
        change.Commit();
    }

    internal void UpdateStructuralCommands()
    {
        var ownerRound = OwnerRound;
        var index = ownerRound?.Themes.IndexOf(this) ?? -1;
        MoveEarlier.CanBeExecuted = index > 0;
        MoveLater.CanBeExecuted = ownerRound != null && index >= 0 && index + 1 < ownerRound.Themes.Count;
        Duplicate.CanBeExecuted = index >= 0;
    }

    private void RemoveTheme_Executed(object? arg)
    {
        var ownerRound = OwnerRound;

        if (ownerRound == null)
        {
            return;
        }

        var ownerDocument = ownerRound.OwnerPackage?.Document;

        if (ownerDocument == null)
        {
            return;
        }

        try
        {
            var index = ownerRound.Themes.IndexOf(this);
            var isActive = ownerDocument.ActiveNode == this;

            using var change = ownerDocument.OperationsManager.BeginComplexChange();
            ownerRound.Themes.Remove(this);
            change.Commit();

            if (isActive)
            {
                ownerDocument.Navigate.Execute(index < ownerRound.Themes.Count ? ownerRound.Themes[index] : ownerRound);
            }
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.Inform(exc.Message, true);
        }
    }

    private void AddQuestion_Executed(object? arg)
    {
        try
        {
            var document = (OwnerRound?.OwnerPackage?.Document) ?? throw new InvalidOperationException("document not found");
            var price = DetectNextQuestionPrice(OwnerRound);

            var question = PackageItemsHelper.CreateQuestion(price);

            var questionViewModel = new QuestionViewModel(question);
            Questions.Add(questionViewModel);

            QDocument.ActivatedObject = questionViewModel.Parameters?.FirstOrDefault().Value.ContentValue?.FirstOrDefault();

            document.Navigate.Execute(questionViewModel);
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.Inform(exc.Message, true);
        }
    }

    internal int DetectNextQuestionPrice(RoundViewModel round)
    {
        var validQuestions = Questions.Where(q => q.Model.Price != Question.InvalidPrice).ToList();
        var questionCount = validQuestions.Count;

        if (questionCount > 1)
        {
            var add = validQuestions[1].Model.Price - validQuestions[0].Model.Price;
            return Math.Max(0, validQuestions[questionCount - 1].Model.Price + add);
        }

        if (questionCount > 0)
        {
            return validQuestions[0].Model.Price * 2;
        }

        var roundIndex = round.OwnerPackage?.Rounds.IndexOf(round) ?? 0;

        return round.Model.Type == RoundTypes.Final ? 0 : AppSettings.Default.QuestionBase * (roundIndex + 1);
    }

    private void AddEmptyQuestion_Executed(object? arg)
    {
        var question = new Question { Price = -1 };
        var questionViewModel = new QuestionViewModel(question);
        Questions.Add(questionViewModel);
    }

    private void SortQuestions_Executed(object? arg)
    {
        try
        {
            var document = (OwnerRound?.OwnerPackage?.Document) ?? throw new InvalidOperationException("document not found");
            using var change = document.OperationsManager.BeginComplexChange();

            for (int i = 1; i < Questions.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (Questions[i].Model.Price < Questions[j].Model.Price)
                    {
                        Questions.Move(i, j);
                        break;
                    }
                }
            }

            change.Commit();
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.Inform(exc.Message, true);
        }
    }

    private void ShuffleQuestions_Executed(object? arg)
    {
        try
        {
            var document = (OwnerRound?.OwnerPackage?.Document) ?? throw new InvalidOperationException("document not found");
            using var change = document.OperationsManager.BeginComplexChange();

            for (int i = 0; i < Questions.Count - 1; i++)
            {
                var j = i + Random.Shared.Next(Questions.Count - i);

                if (i == j)
                {
                    continue;
                }

                (Questions[j].Model.Price, Questions[i].Model.Price) = (Questions[i].Model.Price, Questions[j].Model.Price);
                (Questions[i], Questions[j]) = (Questions[j], Questions[i]);
            }

            change.Commit();
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.Inform(exc.Message, true);
        }
    }

    protected override void UpdateCosts(CostSetter costSetter)
    {
        try
        {
            var document = (OwnerRound?.OwnerPackage?.Document) ?? throw new InvalidOperationException("document not found");
            using var change = document.OperationsManager.BeginComplexChange();

            UpdateCostsCore(costSetter);
            change.Commit();
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.Inform(exc.Message, true);
        }
    }

    public void UpdateCostsCore(CostSetter costSetter)
    {
        for (var i = 0; i < Questions.Count; i++)
        {
            Questions[i].Model.Price = costSetter.BaseValue + costSetter.Increment * i;
        }
    }

    public void TryImportMedia(string filePath)
    {
        try
        {
            for (var i = 0; i < Questions.Count; i++)
            {
                if (Questions[i].Parameters.TryGetValue(QuestionParameterNames.Question, out var existingQuestionParameter)
                    && existingQuestionParameter.ContentValue != null
                    && existingQuestionParameter.ContentValue.Count == 1
                    && existingQuestionParameter.ContentValue[0].Type == ContentTypes.Text
                    && existingQuestionParameter.ContentValue[0].Model.Value.Length == 0)
                {
                    existingQuestionParameter.ContentValue.TryImportMedia(filePath);
                    IsExpanded = true;
                    return;
                }
            }

            var document = (OwnerRound?.OwnerPackage?.Document) ?? throw new InvalidOperationException("document not found");
            var price = DetectNextQuestionPrice(OwnerRound);

            var question = PackageItemsHelper.CreateQuestion(price);

            var questionViewModel = new QuestionViewModel(question);
            Questions.Add(questionViewModel);

            if (questionViewModel.Parameters.TryGetValue(QuestionParameterNames.Question, out var questionParameter))
            {
                questionParameter.ContentValue?.TryImportMedia(filePath);
            }

            IsExpanded = true;
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.Inform(exc.Message, true);
        }
    }
}
