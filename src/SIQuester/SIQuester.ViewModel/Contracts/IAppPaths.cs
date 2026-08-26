namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Provides platform-conventional application directories by responsibility.
/// </summary>
public interface IAppPaths
{
    string ConfigurationDirectory { get; }
    string DataDirectory { get; }
    string CacheDirectory { get; }
    string StateDirectory { get; }
    string LogDirectory { get; }
    string RecoveryDirectory { get; }
    string TemporaryMediaDirectory { get; }
    string TemplatesDirectory { get; }
    string? LegacySettingsFilePath { get; }
}
