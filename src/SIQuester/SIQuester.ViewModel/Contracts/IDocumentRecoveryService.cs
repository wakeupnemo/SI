using SIPackages;

namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Describes one complete recoverable document snapshot.
/// </summary>
public sealed record DocumentRecoveryEntry(
    string RecoveryId,
    string SnapshotPath,
    string? OriginalPath,
    string DisplayName,
    DateTimeOffset SavedAtUtc,
    long SnapshotLength,
    string SnapshotSha256,
    bool IsStale);

/// <summary>
/// Persists complete document snapshots outside canonical package paths.
/// </summary>
public interface IDocumentRecoveryService
{
    ValueTask<DocumentRecoveryEntry> SaveAsync(
        QDocument document,
        string recoveryId,
        string? originalPath,
        string displayName,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<DocumentRecoveryEntry>> ListAsync(
        CancellationToken cancellationToken = default);

    ValueTask<SIDocument> LoadAsync(
        DocumentRecoveryEntry entry,
        CancellationToken cancellationToken = default);

    ValueTask DiscardAsync(string recoveryId, CancellationToken cancellationToken = default);
}
