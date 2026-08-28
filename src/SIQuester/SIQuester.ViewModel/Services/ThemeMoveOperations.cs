using SIPackages;
using SIPackages.Core;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Identifies a theme by its canonical package indexes.
/// </summary>
public readonly record struct ThemeLocation(int RoundIndex, int ThemeIndex);

/// <summary>
/// Contains stable, serializable data for an internal theme drag.
/// </summary>
public sealed record ThemeDragData(
    string DocumentId,
    ThemeLocation Source,
    string ThemeFingerprint);

/// <summary>
/// Defines the outcome of a theme move.
/// </summary>
public enum ThemeMoveResult
{
    Applied,
    NoChange,
    Cancelled,
    InvalidSource,
    InvalidTarget,
}

/// <summary>
/// Moves themes between rounds while preserving canonical collection and price semantics.
/// </summary>
public sealed class ThemeMoveOperations
{
    private readonly QDocument _document;

    internal ThemeMoveOperations(QDocument document) => _document = document;

    /// <summary>
    /// Creates stable drag data for a theme in this document.
    /// </summary>
    public ThemeDragData CreateDragData(ThemeViewModel theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var round = theme.OwnerRound
            ?? throw new ArgumentException("The theme is not attached to a round.", nameof(theme));

        if (!ReferenceEquals(round.OwnerPackage, _document.Package))
        {
            throw new ArgumentException("The theme belongs to another document.", nameof(theme));
        }

        var location = new ThemeLocation(
            _document.Package.Rounds.IndexOf(round),
            round.Themes.IndexOf(theme));

        if (location.RoundIndex < 0 || location.ThemeIndex < 0)
        {
            throw new ArgumentException("The theme is not present in its owner collection.", nameof(theme));
        }

        return new ThemeDragData(
            _document.RecoveryId,
            location,
            CalculateFingerprint(theme.Model));
    }

    /// <summary>
    /// Moves a theme to an insertion slot in a target round.
    /// </summary>
    public ThemeMoveResult Apply(
        ThemeDragData dragData,
        ThemeLocation target,
        bool recalculatePrices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dragData);

        if (cancellationToken.IsCancellationRequested)
        {
            return ThemeMoveResult.Cancelled;
        }

        if (!string.Equals(dragData.DocumentId, _document.RecoveryId, StringComparison.Ordinal)
            || !TryResolveTheme(dragData.Source, out var sourceRound, out var sourceTheme)
            || !string.Equals(
                dragData.ThemeFingerprint,
                CalculateFingerprint(sourceTheme.Model),
                StringComparison.Ordinal))
        {
            return ThemeMoveResult.InvalidSource;
        }

        if (!TryResolveRound(target.RoundIndex, out var targetRound)
            || target.ThemeIndex < 0
            || target.ThemeIndex > targetRound.Themes.Count)
        {
            return ThemeMoveResult.InvalidTarget;
        }

        var sourceIndex = dragData.Source.ThemeIndex;

        if (ReferenceEquals(sourceRound, targetRound))
        {
            var destinationIndex = target.ThemeIndex > sourceIndex
                ? target.ThemeIndex - 1
                : target.ThemeIndex;

            if (destinationIndex == sourceIndex)
            {
                return ThemeMoveResult.NoChange;
            }

            using var change = _document.OperationsManager.BeginComplexChange();
            sourceRound.Themes.Move(sourceIndex, destinationIndex);

            if (recalculatePrices)
            {
                ResetPrices(sourceTheme, targetRound);
            }

            RecordSelectionChange(sourceTheme);
            change.Commit();
            return ThemeMoveResult.Applied;
        }

        var insertedTheme = new ThemeViewModel(sourceTheme.Model.Clone());

        using (var change = _document.OperationsManager.BeginComplexChange())
        {
            targetRound.Themes.Insert(target.ThemeIndex, insertedTheme);

            if (recalculatePrices)
            {
                ResetPrices(insertedTheme, targetRound);
            }

            sourceRound.Themes.RemoveAt(sourceIndex);
            RecordSelectionChange(insertedTheme);
            change.Commit();
        }

        return ThemeMoveResult.Applied;
    }

    private bool TryResolveTheme(
        ThemeLocation location,
        out RoundViewModel round,
        out ThemeViewModel theme)
    {
        theme = null!;

        if (!TryResolveRound(location.RoundIndex, out round)
            || location.ThemeIndex < 0
            || location.ThemeIndex >= round.Themes.Count)
        {
            return false;
        }

        theme = round.Themes[location.ThemeIndex];
        return true;
    }

    private bool TryResolveRound(int roundIndex, out RoundViewModel round)
    {
        round = null!;

        if (roundIndex < 0 || roundIndex >= _document.Package.Rounds.Count)
        {
            return false;
        }

        round = _document.Package.Rounds[roundIndex];
        return true;
    }

    private void ResetPrices(ThemeViewModel theme, RoundViewModel targetRound)
    {
        var roundIndex = _document.Package.Rounds.IndexOf(targetRound);
        var basePrice = targetRound.Model.Type == RoundTypes.Final
            ? 0
            : (roundIndex + 1) * _document.Settings.QuestionBase;

        for (var i = 0; i < theme.Questions.Count; i++)
        {
            theme.Questions[i].Model.Price = basePrice * (i + 1);
        }
    }

    private void RecordSelectionChange(ThemeViewModel theme)
    {
        if (theme.OwnerRound != null)
        {
            theme.OwnerRound.IsExpanded = true;
        }

        var selectionChange = new ActiveNodeSelectionChange(_document, _document.ActiveNode, theme);
        _document.OperationsManager.AddChange(selectionChange);
        selectionChange.Apply();
    }

    private static string CalculateFingerprint(Theme theme)
    {
        var content = new StringBuilder();

        using (var writer = XmlWriter.Create(content, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            theme.WriteXml(writer);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }
}
