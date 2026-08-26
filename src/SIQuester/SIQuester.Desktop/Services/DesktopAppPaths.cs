using SIQuester.Model;
using SIQuester.ViewModel.Contracts;

namespace SIQuester.Desktop.Services;

internal sealed class DesktopAppPaths : IAppPaths
{
    public string ConfigurationDirectory { get; }
    public string DataDirectory { get; }
    public string CacheDirectory { get; }
    public string StateDirectory { get; }
    public string LogDirectory { get; }
    public string RecoveryDirectory { get; }
    public string TemporaryMediaDirectory { get; }
    public string TemplatesDirectory { get; }
    public string? LegacySettingsFilePath { get; }

    public DesktopAppPaths()
    {
        const string applicationName = "SIQuester";
        string? platformLogDirectory = null;

        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.Combine(local, applicationName);
            ConfigurationDirectory = Path.Combine(root, "config");
            DataDirectory = Path.Combine(root, "data");
            CacheDirectory = Path.Combine(root, "cache");
            StateDirectory = Path.Combine(root, "state");
            LegacySettingsFilePath = Path.Combine(
                local,
                AppSettings.ManufacturerName,
                AppSettings.ProductName,
                "Settings",
                "usersettings.json");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var applicationSupport = Path.Combine(home, "Library", "Application Support", applicationName);
            ConfigurationDirectory = Path.Combine(applicationSupport, "Config");
            DataDirectory = applicationSupport;
            CacheDirectory = Path.Combine(home, "Library", "Caches", applicationName);
            StateDirectory = Path.Combine(applicationSupport, "State");
            platformLogDirectory = Path.Combine(home, "Library", "Logs", applicationName);
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            ConfigurationDirectory = ResolveXdg("XDG_CONFIG_HOME", Path.Combine(home, ".config"), applicationName);
            DataDirectory = ResolveXdg("XDG_DATA_HOME", Path.Combine(home, ".local", "share"), applicationName);
            CacheDirectory = ResolveXdg("XDG_CACHE_HOME", Path.Combine(home, ".cache"), applicationName);
            StateDirectory = ResolveXdg("XDG_STATE_HOME", Path.Combine(home, ".local", "state"), applicationName);
        }

        LogDirectory = platformLogDirectory ?? Path.Combine(StateDirectory, "logs");
        RecoveryDirectory = Path.Combine(StateDirectory, "recovery");
        TemporaryMediaDirectory = Path.Combine(CacheDirectory, "media");
        TemplatesDirectory = Path.Combine(DataDirectory, "templates");
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigurationDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(RecoveryDirectory);
        Directory.CreateDirectory(TemporaryMediaDirectory);
        Directory.CreateDirectory(TemplatesDirectory);
    }

    private static string ResolveXdg(string variableName, string fallback, string applicationName)
    {
        var configured = Environment.GetEnvironmentVariable(variableName);
        var root = string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured)
            ? fallback
            : configured;
        return Path.Combine(root, applicationName);
    }
}
