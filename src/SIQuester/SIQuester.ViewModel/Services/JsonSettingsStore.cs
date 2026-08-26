using Microsoft.Extensions.Logging;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Persists versioned non-secret settings with same-directory replacement semantics.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    internal const int CurrentSchemaVersion = 1;
    internal const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IAppPaths _appPaths;
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private JsonObject _topLevelExtras = [];
    private JsonObject _settingsExtras = [];
    private bool _futureSchemaLoaded;
    private bool _retainNextBackup;

    /// <summary>
    /// Initializes a settings store rooted at the supplied application paths.
    /// </summary>
    public JsonSettingsStore(IAppPaths appPaths, ILoggerFactory loggerFactory)
    {
        _appPaths = appPaths;
        _logger = loggerFactory.CreateLogger<JsonSettingsStore>();
    }

    public async ValueTask<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var settingsPath = GetSettingsPath();
            var sourcePath = File.Exists(settingsPath)
                ? settingsPath
                : _appPaths.LegacySettingsFilePath is { } legacyPath && File.Exists(legacyPath)
                    ? legacyPath
                    : null;
            var migratedFromLegacyPath = sourcePath != null
                && !sourcePath.Equals(settingsPath, StringComparison.Ordinal);

            if (sourcePath == null)
            {
                ResetPreservedState();
                var defaults = AppSettings.Create();
                defaults.HasChanges = false;
                return new SettingsLoadResult(defaults, NeedsSave: true, IsReadOnly: false);
            }

            try
            {
                await using var stream = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var rootNode = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
                    ?? throw new JsonException("The settings document is empty.");
                var root = rootNode as JsonObject
                    ?? throw new JsonException("The settings document root must be an object.");

                var hasSchema = TryGetProperty(root, "schemaVersion", out var schemaNode);
                var schemaVersion = hasSchema ? schemaNode?.GetValue<int>() ?? 0 : 0;
                var isLegacy = !hasSchema;
                var settingsObject = isLegacy
                    ? root
                    : GetRequiredObject(root, "settings");
                var hasDesktopTheme = HasProperty(settingsObject, "desktopTheme");
                var settings = settingsObject.Deserialize<AppSettings>(SerializerOptions)
                    ?? throw new JsonException("The settings object could not be deserialized.");

                if (!hasDesktopTheme)
                {
                    settings.DesktopTheme = settings.Theme == ThemeOption.Light
                        ? DesktopThemePreference.Light
                        : DesktopThemePreference.Dark;
                }

                var repaired = Repair(settings) || ContainsRejectedValues(settingsObject);
                CaptureUnknownFields(root, settingsObject, isLegacy);
                _futureSchemaLoaded = schemaVersion > CurrentSchemaVersion;
                _retainNextBackup = false;
                settings.HasChanges = false;

                if (_futureSchemaLoaded)
                {
                    const string diagnostic = "Settings were created by a newer application version and are read-only.";
                    _logger.LogWarning("{Diagnostic} Schema {SchemaVersion}", diagnostic, schemaVersion);
                    return new SettingsLoadResult(settings, NeedsSave: false, IsReadOnly: true, diagnostic);
                }

                return new SettingsLoadResult(
                    settings,
                    NeedsSave: migratedFromLegacyPath || isLegacy || schemaVersion < CurrentSchemaVersion || repaired,
                    IsReadOnly: false,
                    migratedFromLegacyPath || isLegacy ? "Legacy settings were migrated in memory." : null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException
                or InvalidOperationException
                or FormatException
                or OverflowException)
            {
                ResetPreservedState();
                _retainNextBackup = sourcePath.Equals(settingsPath, StringComparison.Ordinal);
                _logger.LogWarning(exception, "Settings could not be loaded; defaults will be used");
                var defaults = AppSettings.Create();
                defaults.HasChanges = false;
                return new SettingsLoadResult(
                    defaults,
                    NeedsSave: true,
                    IsReadOnly: false,
                    "Settings are missing or damaged; defaults are active.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_futureSchemaLoaded)
            {
                throw new InvalidOperationException("Newer-version settings cannot be overwritten safely.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_appPaths.ConfigurationDirectory);
            var settingsPath = GetSettingsPath();
            var temporaryPath = Path.Combine(
                _appPaths.ConfigurationDirectory,
                $".{SettingsFileName}.{Guid.NewGuid():N}.tmp");

            try
            {
                var root = BuildDocument(settings);

                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, root, SerializerOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    await Task.Run(() => stream.Flush(flushToDisk: true), cancellationToken);
                }

                await ValidateTemporaryFileAsync(temporaryPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                CommitTemporaryFile(temporaryPath, settingsPath);
                settings.HasChanges = false;
                _logger.LogInformation("Application settings were committed successfully");
            }
            finally
            {
                TryDeleteFile(temporaryPath, "temporary settings file");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private JsonObject BuildDocument(AppSettings settings)
    {
        var settingsNode = JsonSerializer.SerializeToNode(settings, SerializerOptions) as JsonObject
            ?? throw new JsonException("Settings could not be serialized as an object.");

        RemoveSensitiveFields(settingsNode);
        MergeUnknownFields(settingsNode, _settingsExtras);

        var root = new JsonObject
        {
            ["schemaVersion"] = CurrentSchemaVersion,
            ["settings"] = settingsNode,
        };
        MergeUnknownFields(root, _topLevelExtras);
        RemoveSensitiveFields(root);
        return root;
    }

    private void CaptureUnknownFields(JsonObject root, JsonObject originalSettings, bool isLegacy)
    {
        _topLevelExtras = [];

        if (!isLegacy)
        {
            foreach (var property in root)
            {
                if (!property.Key.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase)
                    && !property.Key.Equals("settings", StringComparison.OrdinalIgnoreCase))
                {
                    _topLevelExtras[property.Key] = property.Value?.DeepClone();
                }
            }
        }

        var knownSettings = JsonSerializer.SerializeToNode(new AppSettings(), SerializerOptions) as JsonObject
            ?? throw new JsonException("Known settings fields could not be enumerated.");
        _settingsExtras = [];

        foreach (var property in originalSettings)
        {
            if (!HasProperty(knownSettings, property.Key) && !IsSensitiveName(property.Key))
            {
                _settingsExtras[property.Key] = property.Value?.DeepClone();
            }
        }
    }

    private static bool Repair(AppSettings settings)
    {
        var defaults = AppSettings.Create();
        var repaired = false;

        if (!Enum.IsDefined(settings.Theme))
        {
            settings.Theme = defaults.Theme;
            repaired = true;
        }

        if (!Enum.IsDefined(settings.DesktopTheme))
        {
            settings.DesktopTheme = defaults.DesktopTheme;
            repaired = true;
        }

        if (settings.Language is not null and not "ru-RU" and not "en-US")
        {
            settings.Language = null;
            repaired = true;
        }

        if (settings.FontSize is < 8 or > 72)
        {
            settings.FontSize = defaults.FontSize;
            repaired = true;
        }

        if (string.IsNullOrWhiteSpace(settings.FontFamily))
        {
            settings.FontFamily = defaults.FontFamily;
            repaired = true;
        }

        settings.GPTApiKey = string.Empty;
        return repaired;
    }

    private static bool ContainsRejectedValues(JsonObject settingsObject)
    {
        if (TryGetProperty(settingsObject, "desktopTheme", out var desktopThemeNode)
            && !IsDefinedEnumValue<DesktopThemePreference>(desktopThemeNode))
        {
            return true;
        }

        return HasNumberOutsideRange(settingsObject, "navigatorPaneWidth", 180, 600)
            || HasNumberOutsideRange(settingsObject, "inspectorPaneWidth", 220, 600);
    }

    private static bool IsDefinedEnumValue<TEnum>(JsonNode? value) where TEnum : struct, Enum
    {
        try
        {
            return value != null && Enum.IsDefined(typeof(TEnum), value.GetValue<int>());
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private static bool HasNumberOutsideRange(
        JsonObject settingsObject,
        string propertyName,
        double minimum,
        double maximum)
    {
        if (!TryGetProperty(settingsObject, propertyName, out var value))
        {
            return false;
        }

        try
        {
            var number = value?.GetValue<double>();
            return number is null || number < minimum || number > maximum;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or OverflowException)
        {
            return true;
        }
    }

    private static async ValueTask ValidateTemporaryFileAsync(
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            temporaryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
            ?? throw new JsonException("The staged settings document root is invalid.");

        if (!TryGetProperty(root, "schemaVersion", out var schemaNode)
            || schemaNode?.GetValue<int>() != CurrentSchemaVersion)
        {
            throw new JsonException("The staged settings document has an invalid schema version.");
        }

        var settingsObject = GetRequiredObject(root, "settings");
        _ = settingsObject.Deserialize<AppSettings>(SerializerOptions)
            ?? throw new JsonException("The staged settings object cannot be deserialized.");
    }

    private void CommitTemporaryFile(string temporaryPath, string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            File.Move(temporaryPath, settingsPath);
            return;
        }

        var backupPath = GetUniqueBackupPath(settingsPath);

        try
        {
            File.Replace(temporaryPath, settingsPath, backupPath, ignoreMetadataErrors: true);

            if (_retainNextBackup)
            {
                _logger.LogWarning("Damaged settings were retained in a backup file");
            }
            else
            {
                TryDeleteFile(backupPath, "obsolete settings backup");
            }

            _retainNextBackup = false;
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(settingsPath, backupPath);

            try
            {
                File.Move(temporaryPath, settingsPath);
            }
            catch
            {
                if (!File.Exists(settingsPath) && File.Exists(backupPath))
                {
                    File.Move(backupPath, settingsPath);
                }

                throw;
            }

            _retainNextBackup = false;
        }
    }

    private static string GetUniqueBackupPath(string settingsPath)
    {
        var basePath = $"{settingsPath}.bak";

        for (var index = 0; ; index++)
        {
            var candidate = index == 0 ? basePath : $"{basePath}.{index}";

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static JsonObject GetRequiredObject(JsonObject root, string propertyName) =>
        TryGetProperty(root, propertyName, out var node) && node is JsonObject value
            ? value
            : throw new JsonException($"Required object '{propertyName}' is missing.");

    private static bool TryGetProperty(JsonObject value, string propertyName, out JsonNode? node)
    {
        foreach (var property in value)
        {
            if (property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                node = property.Value;
                return true;
            }
        }

        node = null;
        return false;
    }

    private static bool HasProperty(JsonObject value, string propertyName) =>
        value.Any(property => property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

    private static void MergeUnknownFields(JsonObject destination, JsonObject extras)
    {
        foreach (var property in extras)
        {
            if (!HasProperty(destination, property.Key) && !IsSensitiveName(property.Key))
            {
                destination[property.Key] = property.Value?.DeepClone();
            }
        }
    }

    private static void RemoveSensitiveFields(JsonNode value)
    {
        if (value is JsonObject objectValue)
        {
            foreach (var property in objectValue.ToArray())
            {
                if (IsSensitiveName(property.Key))
                {
                    objectValue.Remove(property.Key);
                }
                else if (property.Value != null)
                {
                    RemoveSensitiveFields(property.Value);
                }
            }
        }
        else if (value is JsonArray arrayValue)
        {
            foreach (var item in arrayValue)
            {
                if (item != null)
                {
                    RemoveSensitiveFields(item);
                }
            }
        }
    }

    private static bool IsSensitiveName(string name) =>
        name.Contains("apiKey", StringComparison.OrdinalIgnoreCase)
        || name.Contains("api_key", StringComparison.OrdinalIgnoreCase)
        || name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("token", StringComparison.OrdinalIgnoreCase);

    private string GetSettingsPath() => Path.Combine(_appPaths.ConfigurationDirectory, SettingsFileName);

    private void ResetPreservedState()
    {
        _topLevelExtras = [];
        _settingsExtras = [];
        _futureSchemaLoaded = false;
        _retainNextBackup = false;
    }

    private void TryDeleteFile(string path, string purpose)
    {
        try
        {
            TryDeleteFileStatic(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not remove {Purpose}", purpose);
        }
    }

    private static void TryDeleteFileStatic(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void Dispose() => _gate.Dispose();
}
