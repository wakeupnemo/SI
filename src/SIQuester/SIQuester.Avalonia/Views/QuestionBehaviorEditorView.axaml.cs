using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIPackages.Core;
using SIQuester.Avalonia.Localization;
using SIQuester.ViewModel;
using SIQuester.ViewModel.Model;
using System.ComponentModel;

namespace SIQuester.Avalonia.Views;

public sealed record QuestionBehaviorChoice(string TypeName, string DisplayName);

public sealed record NumberSetModeChoice(NumberSetMode Mode, string DisplayName);

/// <summary>
/// Maps localized, high-level authoring choices onto the retained canonical question model.
/// </summary>
public partial class QuestionBehaviorEditorView : UserControl
{
    private const string CustomQuestionType = "custom";

    public static readonly StyledProperty<QuestionViewModel?> QuestionProperty =
        AvaloniaProperty.Register<QuestionBehaviorEditorView, QuestionViewModel?>(nameof(Question));

    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        QuestionTypes.Default,
        QuestionTypes.Simple,
        QuestionTypes.Stake,
        QuestionTypes.StakeAll,
        QuestionTypes.Secret,
        QuestionTypes.SecretPublicPrice,
        QuestionTypes.SecretNoQuestion,
        QuestionTypes.NoRisk,
        QuestionTypes.ForAll,
        CustomQuestionType,
    };

    private QuestionViewModel? _subscribedQuestion;
    private NumberSetEditorNewViewModel? _subscribedPrice;
    private bool _isAttached;
    private bool _isSynchronizingBehavior;
    private bool _isSynchronizingPriceMode;

    public QuestionViewModel? Question
    {
        get => GetValue(QuestionProperty);
        set => SetValue(QuestionProperty, value);
    }

    public IReadOnlyList<NumberSetModeChoice> PriceModes { get; }

    public QuestionBehaviorEditorView()
    {
        PriceModes =
        [
            new(NumberSetMode.FixedValue, UiStrings.SecretPriceFixed),
            new(NumberSetMode.MinimumOrMaximumInRound, UiStrings.SecretPriceRoundExtremes),
            new(NumberSetMode.Range, UiStrings.SecretPriceEndpoints),
            new(NumberSetMode.RangeWithStep, UiStrings.SecretPriceSteppedRange),
        ];

        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        AttachQuestion(Question);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        AttachQuestion(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == QuestionProperty && _isAttached)
        {
            AttachQuestion(Question);
        }
    }

    private void AttachQuestion(QuestionViewModel? question)
    {
        if (_subscribedQuestion != null)
        {
            _subscribedQuestion.PropertyChanged -= Question_PropertyChanged;
        }

        _subscribedQuestion = question;

        if (_subscribedQuestion != null)
        {
            _subscribedQuestion.PropertyChanged += Question_PropertyChanged;
        }

        SyncBehaviorChoices();
        AttachPrice(_subscribedQuestion?.SecretPrice);
    }

    private void AttachPrice(NumberSetEditorNewViewModel? price)
    {
        if (_subscribedPrice != null)
        {
            _subscribedPrice.PropertyChanged -= Price_PropertyChanged;
        }

        _subscribedPrice = price;

        if (_subscribedPrice != null)
        {
            _subscribedPrice.PropertyChanged += Price_PropertyChanged;
        }

        SyncPriceMode();
    }

    private void Question_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuestionViewModel.TypeName))
        {
            SyncBehaviorChoices();
        }

        if (e.PropertyName == nameof(QuestionViewModel.SecretPrice))
        {
            AttachPrice(_subscribedQuestion?.SecretPrice);
        }
    }

    private void Price_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NumberSetEditorNewViewModel.Mode))
        {
            SyncPriceMode();
        }
    }

    private void SyncBehaviorChoices()
    {
        _isSynchronizingBehavior = true;

        try
        {
            var choices = CreateBehaviorChoices(_subscribedQuestion?.TypeName);
            BehaviorSelector.ItemsSource = choices;
            BehaviorSelector.SelectedItem = choices.FirstOrDefault(
                choice => choice.TypeName == _subscribedQuestion?.TypeName);
        }
        finally
        {
            _isSynchronizingBehavior = false;
        }
    }

    private static IReadOnlyList<QuestionBehaviorChoice> CreateBehaviorChoices(string? currentType)
    {
        var choices = new List<QuestionBehaviorChoice>
        {
            new(QuestionTypes.Default, UiStrings.RoundDefaultBehavior),
            new(QuestionTypes.Simple, UiStrings.SimpleBehavior),
            new(QuestionTypes.Stake, UiStrings.StakeBehavior),
            new(QuestionTypes.StakeAll, UiStrings.StakeAllBehavior),
            new(QuestionTypes.Secret, UiStrings.SecretBehavior),
            new(QuestionTypes.SecretPublicPrice, UiStrings.SecretPublicPriceBehavior),
            new(QuestionTypes.SecretNoQuestion, UiStrings.SecretNoQuestionBehavior),
            new(QuestionTypes.NoRisk, UiStrings.NoRiskBehavior),
            new(QuestionTypes.ForAll, UiStrings.ForAllBehavior),
            new(CustomQuestionType, UiStrings.CustomManualBehavior),
        };

        if (currentType != null && !KnownTypes.Contains(currentType))
        {
            choices.Add(new(currentType, string.Format(UiStrings.UnknownQuestionBehavior, currentType)));
        }

        return choices;
    }

    private void SyncPriceMode()
    {
        _isSynchronizingPriceMode = true;

        try
        {
            PriceModeSelector.SelectedItem = _subscribedPrice == null
                ? null
                : PriceModes.First(choice => choice.Mode == _subscribedPrice.Mode);
        }
        finally
        {
            _isSynchronizingPriceMode = false;
        }
    }

    private void BehaviorSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingBehavior
            || _subscribedQuestion == null
            || BehaviorSelector.SelectedItem is not QuestionBehaviorChoice choice
            || choice.TypeName == _subscribedQuestion.TypeName)
        {
            return;
        }

        if (choice.TypeName == QuestionTypes.Default)
        {
            _subscribedQuestion.ClearType.Execute(null);
        }
        else
        {
            _subscribedQuestion.SetQuestionType.Execute(choice.TypeName);
        }
    }

    private void PriceModeSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingPriceMode
            && _subscribedPrice != null
            && PriceModeSelector.SelectedItem is NumberSetModeChoice choice)
        {
            _subscribedPrice.Mode = choice.Mode;
        }
    }
}
