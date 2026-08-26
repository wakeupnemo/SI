namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Owns platform files materialized for external or native media consumers.
/// </summary>
public interface IMediaMaterializationService
{
    /// <summary>
    /// Releases materialized copies associated with package media names.
    /// Implementations must keep this deterministic and non-blocking.
    /// </summary>
    void ReleaseMaterializedMedia(IEnumerable<string> mediaNames);
}
