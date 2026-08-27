namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Describes optional host capabilities without exposing UI-framework types.
/// </summary>
public interface IPlatformCapabilities
{
    /// <summary>
    /// Gets a value indicating whether the host provides per-entry recovery management UI.
    /// </summary>
    bool SupportsRecoveryManagementUi { get; }
}
