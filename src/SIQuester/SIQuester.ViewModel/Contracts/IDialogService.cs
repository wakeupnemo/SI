namespace SIQuester.ViewModel.Contracts;

public enum SaveChangesDecision
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// Describes a selectable dialog action without transferring UI callbacks across layers.
/// </summary>
public sealed record DialogOption(string Id, string Label, string? Description = null);

/// <summary>
/// Presents typed application dialogs asynchronously.
/// </summary>
public interface IDialogService
{
    ValueTask<SaveChangesDecision> ConfirmSaveChangesAsync(
        string message,
        bool allowCancel,
        CancellationToken cancellationToken = default);

    ValueTask<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default);

    ValueTask ShowMessageAsync(string message, CancellationToken cancellationToken = default);

    ValueTask ShowErrorAsync(string message, CancellationToken cancellationToken = default);

    ValueTask<string?> SelectOptionAsync(
        string message,
        IReadOnlyList<DialogOption> options,
        CancellationToken cancellationToken = default);
}
