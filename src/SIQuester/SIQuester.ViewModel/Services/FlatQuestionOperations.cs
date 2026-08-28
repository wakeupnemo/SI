using SIPackages;
using SIPackages.Core;
using SIQuester.Model;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Identifies a question by its canonical package indexes.
/// </summary>
public readonly record struct FlatQuestionLocation(int RoundIndex, int ThemeIndex, int QuestionIndex);

/// <summary>
/// Contains stable, serializable data for an internal flat-editor question drag.
/// </summary>
public sealed record FlatQuestionDragData(
    string DocumentId,
    FlatQuestionLocation Source,
    string QuestionFingerprint);

/// <summary>
/// Defines how a flat-editor question drop is applied.
/// </summary>
public enum FlatQuestionDropMode
{
    Move,
    Copy,
}

/// <summary>
/// Defines the outcome of a flat-editor question drop.
/// </summary>
public enum FlatQuestionDropResult
{
    Applied,
    NoChange,
    Cancelled,
    InvalidSource,
    InvalidTarget,
}

/// <summary>
/// Applies flat-editor question operations to canonical document collections.
/// </summary>
public sealed class FlatQuestionOperations
{
    private readonly QDocument _document;

    internal FlatQuestionOperations(QDocument document) => _document = document;

    /// <summary>
    /// Creates serializable drag data for a question in this document.
    /// </summary>
    public FlatQuestionDragData CreateDragData(QuestionViewModel question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var theme = question.OwnerTheme
            ?? throw new ArgumentException("The question is not attached to a theme.", nameof(question));
        var round = theme.OwnerRound
            ?? throw new ArgumentException("The question theme is not attached to a round.", nameof(question));

        if (!ReferenceEquals(round.OwnerPackage, _document.Package))
        {
            throw new ArgumentException("The question belongs to another document.", nameof(question));
        }

        var location = new FlatQuestionLocation(
            _document.Package.Rounds.IndexOf(round),
            round.Themes.IndexOf(theme),
            theme.Questions.IndexOf(question));

        if (location.RoundIndex < 0 || location.ThemeIndex < 0 || location.QuestionIndex < 0)
        {
            throw new ArgumentException("The question is not present in its owner collection.", nameof(question));
        }

        return new FlatQuestionDragData(
            _document.RecoveryId,
            location,
            CalculateFingerprint(question.Model));
    }

    /// <summary>
    /// Moves or copies a question to an insertion slot in a target theme.
    /// </summary>
    /// <remarks>
    /// <paramref name="target"/>'s question index is an insertion index in the range
    /// zero through the target question count, inclusive.
    /// </remarks>
    public FlatQuestionDropResult Apply(
        FlatQuestionDragData dragData,
        FlatQuestionLocation target,
        FlatQuestionDropMode mode,
        bool recalculatePrices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dragData);

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return FlatQuestionDropResult.Cancelled;
        }

        if (!string.Equals(dragData.DocumentId, _document.RecoveryId, StringComparison.Ordinal)
            || !TryResolveQuestion(dragData.Source, out var sourceTheme, out var sourceQuestion)
            || !string.Equals(
                dragData.QuestionFingerprint,
                CalculateFingerprint(sourceQuestion.Model),
                StringComparison.Ordinal))
        {
            return FlatQuestionDropResult.InvalidSource;
        }

        if (!TryResolveTheme(target, out var targetTheme)
            || target.QuestionIndex < 0
            || target.QuestionIndex > targetTheme.Questions.Count)
        {
            return FlatQuestionDropResult.InvalidTarget;
        }

        var sourceIndex = dragData.Source.QuestionIndex;

        if (mode == FlatQuestionDropMode.Move && ReferenceEquals(sourceTheme, targetTheme))
        {
            var destinationIndex = target.QuestionIndex > sourceIndex
                ? target.QuestionIndex - 1
                : target.QuestionIndex;

            if (destinationIndex == sourceIndex)
            {
                return FlatQuestionDropResult.NoChange;
            }

            var prices = recalculatePrices ? CapturePrices(sourceTheme) : null;

            using var change = _document.OperationsManager.BeginComplexChange();
            sourceTheme.Questions.Move(sourceIndex, destinationIndex);

            if (prices != null)
            {
                ResetPrices(sourceTheme, prices);
            }

            RecordSelectionChange(sourceQuestion);
            change.Commit();
            return FlatQuestionDropResult.Applied;
        }

        var sourcePrices = recalculatePrices && mode == FlatQuestionDropMode.Move
            ? CapturePrices(sourceTheme)
            : null;
        var targetPrices = recalculatePrices ? CapturePrices(targetTheme) : null;
        var insertedQuestion = new QuestionViewModel(sourceQuestion.Model.Clone());

        using (var change = _document.OperationsManager.BeginComplexChange())
        {
            targetTheme.Questions.Insert(target.QuestionIndex, insertedQuestion);

            if (targetPrices != null)
            {
                ResetPrices(targetTheme, targetPrices);
            }

            if (mode == FlatQuestionDropMode.Move)
            {
                sourceTheme.Questions.RemoveAt(sourceIndex);

                if (sourcePrices != null)
                {
                    ResetPrices(sourceTheme, sourcePrices);
                }
            }

            RecordSelectionChange(insertedQuestion);
            change.Commit();
        }

        return FlatQuestionDropResult.Applied;
    }

    /// <summary>
    /// Moves a question one position backward or forward inside its current theme.
    /// </summary>
    /// <param name="question">Question to move.</param>
    /// <param name="offset">Must be <c>-1</c> for backward or <c>1</c> for forward.</param>
    /// <param name="recalculatePrices">Whether canonical question prices should remain attached to positions.</param>
    public FlatQuestionDropResult MoveWithinTheme(
        QuestionViewModel question,
        int offset,
        bool recalculatePrices)
    {
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var dragData = CreateDragData(question);
        var source = dragData.Source;
        var theme = question.OwnerTheme!;

        if ((offset < 0 && source.QuestionIndex == 0)
            || (offset > 0 && source.QuestionIndex == theme.Questions.Count - 1))
        {
            return FlatQuestionDropResult.NoChange;
        }

        var insertionIndex = offset < 0
            ? source.QuestionIndex - 1
            : source.QuestionIndex + 2;

        return Apply(
            dragData,
            source with { QuestionIndex = insertionIndex },
            FlatQuestionDropMode.Move,
            recalculatePrices);
    }

    /// <summary>
    /// Duplicates a question immediately after the source question.
    /// </summary>
    public FlatQuestionDropResult DuplicateAfter(
        QuestionViewModel question,
        bool recalculatePrices)
    {
        var dragData = CreateDragData(question);
        return Apply(
            dragData,
            dragData.Source with { QuestionIndex = dragData.Source.QuestionIndex + 1 },
            FlatQuestionDropMode.Copy,
            recalculatePrices);
    }

    private bool TryResolveQuestion(
        FlatQuestionLocation location,
        out ThemeViewModel theme,
        out QuestionViewModel question)
    {
        question = null!;

        if (!TryResolveTheme(location, out theme)
            || location.QuestionIndex < 0
            || location.QuestionIndex >= theme.Questions.Count)
        {
            return false;
        }

        question = theme.Questions[location.QuestionIndex];
        return true;
    }

    private void RecordSelectionChange(QuestionViewModel question)
    {
        var selectionChange = new ActiveNodeSelectionChange(_document, _document.ActiveNode, question);
        _document.OperationsManager.AddChange(selectionChange);
        selectionChange.Apply();
    }

    private bool TryResolveTheme(FlatQuestionLocation location, out ThemeViewModel theme)
    {
        theme = null!;

        if (location.RoundIndex < 0 || location.RoundIndex >= _document.Package.Rounds.Count)
        {
            return false;
        }

        var round = _document.Package.Rounds[location.RoundIndex];

        if (location.ThemeIndex < 0 || location.ThemeIndex >= round.Themes.Count)
        {
            return false;
        }

        theme = round.Themes[location.ThemeIndex];
        return true;
    }

    private static int[] CapturePrices(ThemeViewModel theme) =>
        theme.Questions
            .Where(question => question.Model.Price != Question.InvalidPrice)
            .Select(question => question.Model.Price)
            .ToArray();

    private static void ResetPrices(ThemeViewModel theme, IReadOnlyList<int> prices)
    {
        var previousIndex = 0;

        for (var i = 0; i < theme.Questions.Count; i++)
        {
            var question = theme.Questions[i].Model;

            if (question.Price == Question.InvalidPrice)
            {
                continue;
            }

            if (previousIndex < prices.Count)
            {
                question.Price = prices[previousIndex++];
            }
            else if (i == 0)
            {
                question.Price = AppSettings.Default.QuestionBase;
            }
            else if (i == 1)
            {
                question.Price = theme.Questions[0].Model.Price * 2;
            }
            else
            {
                var previousPrice = theme.Questions[i - 1].Model.Price;
                var delta = previousPrice - theme.Questions[i - 2].Model.Price;
                question.Price = delta > 0 ? previousPrice + delta : previousPrice;
            }
        }
    }

    private static string CalculateFingerprint(InfoOwner item)
    {
        var content = new StringBuilder();

        using (var writer = XmlWriter.Create(content, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            item.WriteXml(writer);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }
}
