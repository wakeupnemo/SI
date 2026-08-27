namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Opens trusted external locations through the desktop shell.
/// </summary>
public interface IExternalLauncher
{
    /// <summary>
    /// Reveals a local file in the platform file manager.
    /// </summary>
    ValueTask RevealFileAsync(string path, CancellationToken cancellationToken = default);
}
