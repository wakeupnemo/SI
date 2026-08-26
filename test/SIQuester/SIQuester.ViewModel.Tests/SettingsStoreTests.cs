using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Services;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class SettingsStoreTests
{
    [Test]
    public async Task LoadAsync_MissingFile_ReturnsWritableDefaults()
    {
        using var paths = new TemporaryAppPaths();
        using var store = new JsonSettingsStore(paths, NullLoggerFactory.Instance);

        var result = await store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.NeedsSave, Is.True);
            Assert.That(result.IsReadOnly, Is.False);
            Assert.That(result.Settings.DesktopTheme, Is.EqualTo(DesktopThemePreference.System));
            Assert.That(result.Settings.NavigatorPaneWidth, Is.EqualTo(280));
            Assert.That(File.Exists(paths.SettingsPath), Is.False);
        });
    }

    [Test]
    public async Task SaveAndLoadAsync_CurrentSchema_RoundTripsSettings()
    {
        using var paths = new TemporaryAppPaths();
        using (var store = new JsonSettingsStore(paths, NullLoggerFactory.Instance))
        {
            var settings = AppSettings.Create();
            settings.Language = "ru-RU";
            settings.DesktopTheme = DesktopThemePreference.Dark;
            settings.NavigatorPaneWidth = 344.5;
            settings.InspectorPaneWidth = 376.25;

            await store.SaveAsync(settings);
        }

        using var reloadedStore = new JsonSettingsStore(paths, NullLoggerFactory.Instance);
        var result = await reloadedStore.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.NeedsSave, Is.False);
            Assert.That(result.IsReadOnly, Is.False);
            Assert.That(result.Settings.Language, Is.EqualTo("ru-RU"));
            Assert.That(result.Settings.DesktopTheme, Is.EqualTo(DesktopThemePreference.Dark));
            Assert.That(result.Settings.NavigatorPaneWidth, Is.EqualTo(344.5));
            Assert.That(result.Settings.InspectorPaneWidth, Is.EqualTo(376.25));
            Assert.That(result.Settings.HasChanges, Is.False);
        });
    }

    [Test]
    public async Task LoadAndSaveAsync_LegacyJson_MigratesThemeAndPreservesUnknownNonSecretFields()
    {
        using var paths = new TemporaryAppPaths();
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        var legacySettings = AppSettings.Create();
        legacySettings.Theme = ThemeOption.DarkGray;
        legacySettings.Language = "en-US";
        var legacy = JsonSerializer.SerializeToNode(legacySettings)!.AsObject();
        legacy.Remove(nameof(AppSettings.DesktopTheme));
        legacy["FutureToggle"] = new JsonObject
        {
            ["value"] = 42,
            ["nested"] = new JsonObject { ["accessToken"] = "nested-secret" },
        };
        legacy["GPTApiKey"] = "must-not-survive";
        Directory.CreateDirectory(Path.GetDirectoryName(paths.LegacySettingsPath)!);
        await File.WriteAllTextAsync(paths.LegacySettingsPath, legacy.ToJsonString());

        using var store = new JsonSettingsStore(paths, NullLoggerFactory.Instance);
        var result = await store.LoadAsync();
        await store.SaveAsync(result.Settings);
        var savedText = await File.ReadAllTextAsync(paths.SettingsPath);
        var migrated = JsonNode.Parse(savedText)!.AsObject();
        var savedSettings = migrated["settings"]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(result.NeedsSave, Is.True);
            Assert.That(result.Settings.DesktopTheme, Is.EqualTo(DesktopThemePreference.Dark));
            Assert.That(migrated["schemaVersion"]!.GetValue<int>(), Is.EqualTo(JsonSettingsStore.CurrentSchemaVersion));
            Assert.That(savedSettings["FutureToggle"]!["value"]!.GetValue<int>(), Is.EqualTo(42));
            Assert.That(ContainsProperty(savedSettings, "GPTApiKey"), Is.False);
            Assert.That(savedText, Does.Not.Contain("must-not-survive"));
            Assert.That(savedText, Does.Not.Contain("nested-secret"));
            Assert.That(File.Exists(paths.LegacySettingsPath), Is.True);
        });
    }

    [Test]
    public async Task LoadAsync_CorruptJson_PreservesOriginalAndReturnsDiagnosticDefaults()
    {
        using var paths = new TemporaryAppPaths();
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        var corruptBytes = Encoding.UTF8.GetBytes("{ not-valid-json");
        await File.WriteAllBytesAsync(paths.SettingsPath, corruptBytes);
        using var store = new JsonSettingsStore(paths, NullLoggerFactory.Instance);

        var result = await store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.NeedsSave, Is.True);
            Assert.That(result.IsReadOnly, Is.False);
            Assert.That(result.Diagnostic, Is.Not.Empty);
            Assert.That(File.ReadAllBytes(paths.SettingsPath), Is.EqualTo(corruptBytes));
        });
    }

    [Test]
    public async Task SaveAsync_AfterCorruptLoad_RetainsOriginalAsBackup()
    {
        using var paths = new TemporaryAppPaths();
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        var corruptBytes = Encoding.UTF8.GetBytes("{ user-content-that-needs-recovery");
        await File.WriteAllBytesAsync(paths.SettingsPath, corruptBytes);
        using var store = new JsonSettingsStore(paths, NullLoggerFactory.Instance);
        var result = await store.LoadAsync();

        await store.SaveAsync(result.Settings);

        var backups = Directory.GetFiles(paths.ConfigurationDirectory, "settings.json.bak*");
        Assert.Multiple(() =>
        {
            Assert.That(backups, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllBytes(backups[0]), Is.EqualTo(corruptBytes));
            Assert.That(
                JsonNode.Parse(File.ReadAllText(paths.SettingsPath))?["schemaVersion"]?.GetValue<int>(),
                Is.EqualTo(JsonSettingsStore.CurrentSchemaVersion));
        });
    }

    [Test]
    public async Task SaveAsync_ReplacingValidSettings_DoesNotLeaveBackupOrTemporaryFiles()
    {
        using var paths = new TemporaryAppPaths();
        using var store = new JsonSettingsStore(paths, NullLoggerFactory.Instance);
        var settings = AppSettings.Create();
        settings.Language = "en-US";
        await store.SaveAsync(settings);
        settings.Language = "ru-RU";

        await store.SaveAsync(settings);

        var files = Directory.GetFiles(paths.ConfigurationDirectory).Select(Path.GetFileName).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(files, Is.EquivalentTo(new[] { JsonSettingsStore.SettingsFileName }));
            Assert.That(
                JsonNode.Parse(File.ReadAllText(paths.SettingsPath))?["settings"]?["language"]?.GetValue<string>(),
                Is.EqualTo("ru-RU"));
        });
    }

    [Test]
    public async Task LoadAsync_InvalidValues_AreRepairedAndMarkedForSave()
    {
        using var paths = new TemporaryAppPaths();
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        var settings = JsonSerializer.SerializeToNode(AppSettings.Create())!.AsObject();
        settings["desktopTheme"] = 999;
        settings["navigatorPaneWidth"] = 12;
        settings["inspectorPaneWidth"] = 9000;
        settings["language"] = "unsupported";
        var root = new JsonObject
        {
            ["schemaVersion"] = JsonSettingsStore.CurrentSchemaVersion,
            ["settings"] = settings,
        };
        await File.WriteAllTextAsync(paths.SettingsPath, root.ToJsonString());
        using var store = new JsonSettingsStore(paths, NullLoggerFactory.Instance);

        var result = await store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.NeedsSave, Is.True);
            Assert.That(result.Settings.DesktopTheme, Is.EqualTo(DesktopThemePreference.System));
            Assert.That(result.Settings.NavigatorPaneWidth, Is.EqualTo(280));
            Assert.That(result.Settings.InspectorPaneWidth, Is.EqualTo(300));
            Assert.That(result.Settings.Language, Is.Null);
        });
    }

    [Test]
    public async Task SaveAsync_PreCancelled_DoesNotChangeExistingFile()
    {
        using var paths = new TemporaryAppPaths();
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        var originalBytes = Encoding.UTF8.GetBytes("existing-settings");
        await File.WriteAllBytesAsync(paths.SettingsPath, originalBytes);
        using var store = new JsonSettingsStore(paths, NullLoggerFactory.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await store.SaveAsync(AppSettings.Create(), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(File.ReadAllBytes(paths.SettingsPath), Is.EqualTo(originalBytes));
    }

    [Test]
    public async Task LoadAsync_FutureSchema_IsReadOnlyAndCannotBeOverwritten()
    {
        using var paths = new TemporaryAppPaths();
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        var root = new JsonObject
        {
            ["schemaVersion"] = JsonSettingsStore.CurrentSchemaVersion + 1,
            ["settings"] = JsonSerializer.SerializeToNode(AppSettings.Create()),
            ["futureMetadata"] = "preserve",
        };
        await File.WriteAllTextAsync(paths.SettingsPath, root.ToJsonString());
        var originalBytes = await File.ReadAllBytesAsync(paths.SettingsPath);
        using var store = new JsonSettingsStore(paths, NullLoggerFactory.Instance);

        var result = await store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsReadOnly, Is.True);
            Assert.That(
                async () => await store.SaveAsync(result.Settings),
                Throws.InstanceOf<InvalidOperationException>());
            Assert.That(File.ReadAllBytes(paths.SettingsPath), Is.EqualTo(originalBytes));
        });
    }

    private static bool ContainsProperty(JsonObject value, string propertyName) =>
        value.Any(property => property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

    private sealed class TemporaryAppPaths : IAppPaths, IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"siquester-settings-tests-{Guid.NewGuid():N}");

        public string ConfigurationDirectory => Path.Combine(_root, "config with spaces", "Настройки");
        public string DataDirectory => Path.Combine(_root, "data");
        public string CacheDirectory => Path.Combine(_root, "cache");
        public string StateDirectory => Path.Combine(_root, "state");
        public string LogDirectory => Path.Combine(StateDirectory, "logs");
        public string RecoveryDirectory => Path.Combine(StateDirectory, "recovery");
        public string TemporaryMediaDirectory => Path.Combine(CacheDirectory, "media");
        public string TemplatesDirectory => Path.Combine(DataDirectory, "templates");
        public string? LegacySettingsFilePath => LegacySettingsPath;
        public string SettingsPath => Path.Combine(ConfigurationDirectory, JsonSettingsStore.SettingsFileName);
        public string LegacySettingsPath => Path.Combine(_root, "legacy", "usersettings.json");

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
