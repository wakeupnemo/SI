using SIQuester.ViewModel.Contracts;

namespace SIQuester.ViewModel.Tests.Mocks;

internal sealed class TestAppPaths : IAppPaths
{
    public TestAppPaths(string? root = null)
    {
        var baseDirectory = root ?? Path.Combine(
            Path.GetTempPath(),
            "SIQuester.Tests",
            Guid.NewGuid().ToString("N"));
        ConfigurationDirectory = Path.Combine(baseDirectory, "config");
        DataDirectory = Path.Combine(baseDirectory, "data");
        CacheDirectory = Path.Combine(baseDirectory, "cache");
        StateDirectory = Path.Combine(baseDirectory, "state");
        LogDirectory = Path.Combine(StateDirectory, "logs");
        RecoveryDirectory = Path.Combine(StateDirectory, "recovery");
        TemporaryMediaDirectory = Path.Combine(CacheDirectory, "media");
        TemplatesDirectory = Path.Combine(DataDirectory, "templates");
    }

    public string ConfigurationDirectory { get; }
    public string DataDirectory { get; }
    public string CacheDirectory { get; }
    public string StateDirectory { get; }
    public string LogDirectory { get; }
    public string RecoveryDirectory { get; }
    public string TemporaryMediaDirectory { get; }
    public string TemplatesDirectory { get; }
    public string? LegacySettingsFilePath => null;
}
