using SIQuester.Model;

namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Describes the result of loading versioned application settings.
/// </summary>
public sealed record SettingsLoadResult(
    AppSettings Settings,
    bool NeedsSave,
    bool IsReadOnly,
    string? Diagnostic = null);

/// <summary>
/// Loads and atomically persists non-secret application settings.
/// </summary>
public interface ISettingsStore
{
    ValueTask<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
