using SIPackages;
using SIQuester.Model;
using SIQuester.ViewModel.Helpers;
using SIQuester.ViewModel.Properties;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Defines a round view model.
/// </summary>
public sealed class RoundViewModel : ItemViewModel<Round>
{
    private PackageViewModel? _ownerPackage;

    public PackageViewModel? OwnerPackage
    {
        get => _ownerPackage;
        set
        {
            if (!ReferenceEquals(_ownerPackage, value))
            {
                _ownerPackage = value;
                UpdateStructuralCommands();
            }
        }
    }

    public override IItemViewModel? Owner => OwnerPackage;

    public ObservableCollection<ThemeViewModel> Themes { get; } = new();

    public override ICommand Add { get; protected set; }

    public override string AddHeader => Resources.AddTheme;

    public override ICommand? Remove { get; protected set; }

    public ICommand Clone { get; }

    public SimpleCommand Duplicate { get; }

    public SimpleCommand MoveEarlier { get; }

    public SimpleCommand MoveLater { get; }

    public ICommand AddTheme { get; private set; }

    public SimpleCommand SetType { get; }

    public RoundViewModel(Round round)
        : base(round)
    {
        foreach (var theme in round.Themes)
        {
            Themes.Add(new ThemeViewModel(theme) { OwnerRound = this });
        }

        Themes.CollectionChanged += Themes_CollectionChanged;

        Clone = new SimpleCommand(CloneRound_Executed);
        Duplicate = new SimpleCommand(DuplicateRound_Executed);
        MoveEarlier = new SimpleCommand(_ => Move(-1));
        MoveLater = new SimpleCommand(_ => Move(1));
        Remove = new SimpleCommand(RemoveRound_Executed);
        Add = AddTheme = new SimpleCommand(AddTheme_Executed);
        SetType = new SimpleCommand(SetType_Executed);
        UpdateStructuralCommands();
    }

    private void SetType_Executed(object? arg)
    {
        if (arg is string type && !string.IsNullOrWhiteSpace(type))
        {
            Model.Type = type;
        }
    }

    private void Themes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
            case NotifyCollectionChangedAction.Replace:
                for (int i = e.NewStartingIndex; i < e.NewStartingIndex + e.NewItems.Count; i++)
                {
                    if (Themes[i].OwnerRound != null)
                        throw new Exception("An attempt was made to add an already bound theme");

                    Themes[i].OwnerRound = this;
                    Model.Themes.Insert(i, Themes[i].Model);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                foreach (ThemeViewModel theme in e.OldItems)
                {
                    theme.OwnerRound = null;
                    Model.Themes.RemoveAt(e.OldStartingIndex);

                    OwnerPackage.Document.ClearLinks(theme);
                }
                break;

            case NotifyCollectionChangedAction.Move:
                var movedTheme = Model.Themes[e.OldStartingIndex];
                Model.Themes.RemoveAt(e.OldStartingIndex);
                Model.Themes.Insert(e.NewStartingIndex, movedTheme);
                break;

            case NotifyCollectionChangedAction.Reset:
                Model.Themes.Clear();
                foreach (ThemeViewModel question in Themes)
                {
                    question.OwnerRound = this;
                    Model.Themes.Add(question.Model);
                }
                break;
        }

        foreach (var theme in Themes)
        {
            theme.UpdateStructuralCommands();
        }
    }

    private void CloneRound_Executed(object? arg)
    {
        if (OwnerPackage == null)
        {
            return;
        }

        var newRoundViewModel = new RoundViewModel(Model.Clone());
        OwnerPackage.Rounds.Add(newRoundViewModel);
        OwnerPackage.Document.Navigate.Execute(newRoundViewModel);
    }

    private void DuplicateRound_Executed(object? arg)
    {
        if (OwnerPackage == null)
        {
            return;
        }

        var index = OwnerPackage.Rounds.IndexOf(this);

        if (index < 0)
        {
            return;
        }

        var newRoundViewModel = new RoundViewModel(Model.Clone());
        using var change = OwnerPackage.Document.OperationsManager.BeginComplexChange();
        OwnerPackage.Rounds.Insert(index + 1, newRoundViewModel);
        change.Commit();
        OwnerPackage.Document.Navigate.Execute(newRoundViewModel);
    }

    private void Move(int offset)
    {
        if (OwnerPackage == null)
        {
            return;
        }

        var sourceIndex = OwnerPackage.Rounds.IndexOf(this);
        var targetIndex = sourceIndex + offset;

        if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= OwnerPackage.Rounds.Count)
        {
            return;
        }

        using var change = OwnerPackage.Document.OperationsManager.BeginComplexChange();
        OwnerPackage.Rounds.Move(sourceIndex, targetIndex);
        change.Commit();
    }

    internal void UpdateStructuralCommands()
    {
        var ownerPackage = OwnerPackage;
        var index = ownerPackage?.Rounds.IndexOf(this) ?? -1;
        MoveEarlier.CanBeExecuted = index > 0;
        MoveLater.CanBeExecuted = ownerPackage != null && index >= 0 && index + 1 < ownerPackage.Rounds.Count;
        Duplicate.CanBeExecuted = index >= 0;
    }

    private void RemoveRound_Executed(object? arg)
    {
        var ownerPackage = OwnerPackage;

        if (ownerPackage == null)
        {
            return;
        }

        var ownerDocument = ownerPackage.Document;

        try
        {
            var index = ownerPackage.Rounds.IndexOf(this);
            var isActive = ownerDocument.ActiveNode == this;

            using var change = ownerDocument.OperationsManager.BeginComplexChange();
            ownerPackage.Rounds.Remove(this);
            change.Commit();

            if (isActive)
            {
                ownerDocument.Navigate.Execute(index < ownerPackage.Rounds.Count ? ownerPackage.Rounds[index] : ownerPackage);
            }
        }
        catch (Exception ex)
        {
            PlatformSpecific.PlatformManager.Instance.ShowErrorMessage(ex.Message);
        }
    }

    private void AddTheme_Executed(object? arg)
    {
        var document = OwnerPackage.Document;

        try
        {
            var theme = new Theme { Name = "" };
            var themeViewModel = new ThemeViewModel(theme);

            using (var change = document.OperationsManager.BeginComplexChange())
            {
                Themes.Add(themeViewModel);

                if (AppSettings.Default.CreateQuestionsWithTheme)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var question = PackageItemsHelper.CreateQuestion((i + 1) * AppSettings.Default.QuestionBase);
                        themeViewModel.Questions.Add(new QuestionViewModel(question));
                    }
                }

                change.Commit();
            }

            QDocument.ActivatedObject = themeViewModel;
            document.Navigate.Execute(themeViewModel);
        }
        catch (Exception exc)
        {
            PlatformSpecific.PlatformManager.Instance.Inform(exc.Message, true);
        }
    }

    protected override void UpdateCosts(CostSetter costSetter)
    {
        var document = OwnerPackage.Document;

        using var change = document.OperationsManager.BeginComplexChange();

        base.UpdateCosts(costSetter);

        foreach (var th in Themes)
        {
            th.UpdateCostsCore(costSetter);
        }

        change.Commit();
    }
}
