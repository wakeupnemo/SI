namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Persists an editor document using validated, failure-safe replacement semantics.
/// </summary>
public interface IDocumentPersistenceService
{
    ValueTask SaveAsync(QDocument document, string destinationPath, CancellationToken cancellationToken = default);
}
