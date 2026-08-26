using Microsoft.Extensions.Logging;
using SIPackages;
using SIQuester.ViewModel.Contracts;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Stores complete, validated SIQ recovery generations with an atomic metadata pointer.
/// </summary>
internal sealed class DocumentRecoveryService : IDocumentRecoveryService
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumMetadataLength = 64 * 1024;
    private const string MetadataFileName = "recovery.json";
    private const string SnapshotPrefix = "document.";
    private const string SnapshotSuffix = ".siq";

    private readonly IAppPaths _appPaths;
    private readonly ILogger<DocumentRecoveryService> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public DocumentRecoveryService(IAppPaths appPaths, ILoggerFactory loggerFactory)
    {
        _appPaths = appPaths;
        _logger = loggerFactory.CreateLogger<DocumentRecoveryService>();
    }

    public async ValueTask<DocumentRecoveryEntry> SaveAsync(
        QDocument document,
        string recoveryId,
        string? originalPath,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateRecoveryId(recoveryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        cancellationToken.ThrowIfCancellationRequested();

        var recoveryDirectory = GetRecoveryDirectory(recoveryId);
        Directory.CreateDirectory(recoveryDirectory);

        var generation = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}.{Guid.NewGuid():N}";
        var snapshotFileName = $"{SnapshotPrefix}{generation}{SnapshotSuffix}";
        var snapshotPath = Path.Combine(recoveryDirectory, snapshotFileName);
        var snapshotStagingPath = Path.Combine(recoveryDirectory, $".{snapshotFileName}.tmp");
        var metadataPath = Path.Combine(recoveryDirectory, MetadataFileName);
        var metadataStagingPath = Path.Combine(recoveryDirectory, $".{MetadataFileName}.{Guid.NewGuid():N}.tmp");
        var metadataCommitted = false;

        try
        {
            await DocumentSnapshotWriter.WriteNewAsync(document, snapshotStagingPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(snapshotStagingPath, snapshotPath);

            var snapshotInfo = new FileInfo(snapshotPath);
            var snapshotHash = await ComputeSha256Async(snapshotPath, cancellationToken);
            var savedAtUtc = DateTimeOffset.UtcNow;
            var metadata = new RecoveryMetadata(
                CurrentSchemaVersion,
                recoveryId,
                snapshotFileName,
                string.IsNullOrWhiteSpace(originalPath) ? null : Path.GetFullPath(originalPath),
                displayName,
                savedAtUtc,
                snapshotInfo.Length,
                snapshotHash);

            await WriteMetadataAsync(metadataStagingPath, metadata, cancellationToken);
            ValidateMetadataFile(metadataStagingPath, recoveryId);
            CommitMetadata(metadataStagingPath, metadataPath);
            metadataCommitted = true;
            DeleteObsoleteSnapshots(recoveryDirectory, snapshotPath);

            _logger.LogInformation(
                "Document recovery generation {RecoveryId} was committed ({SnapshotLength} bytes)",
                recoveryId,
                snapshotInfo.Length);

            return CreateEntry(metadata, snapshotPath);
        }
        finally
        {
            TryDeleteFile(snapshotStagingPath, "snapshot staging file");
            TryDeleteFile(metadataStagingPath, "metadata staging file");

            if (!metadataCommitted)
            {
                TryDeleteFile(snapshotPath, "uncommitted recovery snapshot");
            }
        }
    }

    public async ValueTask<IReadOnlyList<DocumentRecoveryEntry>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_appPaths.RecoveryDirectory);
        var entries = new List<DocumentRecoveryEntry>();

        foreach (var directory in Directory.EnumerateDirectories(_appPaths.RecoveryDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recoveryId = Path.GetFileName(directory);

            try
            {
                ValidateRecoveryId(recoveryId);
                var metadata = await ReadMetadataAsync(
                    Path.Combine(directory, MetadataFileName),
                    recoveryId,
                    cancellationToken);
                var snapshotPath = ResolveSnapshotPath(directory, metadata.SnapshotFileName);
                await ValidateSnapshotAsync(snapshotPath, metadata, cancellationToken);
                var isStale = await IsCanonicalNewerAndValidAsync(metadata, cancellationToken);
                entries.Add(CreateEntry(metadata, snapshotPath, isStale));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Ignoring invalid recovery entry {RecoveryId}; its files were retained",
                    recoveryId);
            }
        }

        return entries
            .OrderByDescending(entry => entry.SavedAtUtc)
            .ToArray();
    }

    public async ValueTask<SIDocument> LoadAsync(
        DocumentRecoveryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateRecoveryId(entry.RecoveryId);
        cancellationToken.ThrowIfCancellationRequested();

        var expectedDirectory = GetRecoveryDirectory(entry.RecoveryId);
        var snapshotPath = Path.GetFullPath(entry.SnapshotPath);

        if (!IsDirectChild(expectedDirectory, snapshotPath))
        {
            throw new InvalidDataException("The recovery snapshot path is outside its recovery directory.");
        }

        var actualHash = await ComputeSha256Async(snapshotPath, cancellationToken);

        if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, entry.SnapshotSha256))
        {
            throw new InvalidDataException("The recovery snapshot hash does not match its metadata.");
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = new FileStream(
                snapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);

            try
            {
                var document = SIDocument.Load(stream);
                cancellationToken.ThrowIfCancellationRequested();
                return document;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }, cancellationToken);
    }

    public ValueTask DiscardAsync(string recoveryId, CancellationToken cancellationToken = default)
    {
        ValidateRecoveryId(recoveryId);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = GetRecoveryDirectory(recoveryId);

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
            _logger.LogInformation("Document recovery entry {RecoveryId} was discarded", recoveryId);
        }

        return ValueTask.CompletedTask;
    }

    private static DocumentRecoveryEntry CreateEntry(
        RecoveryMetadata metadata,
        string snapshotPath,
        bool isStale = false) => new(
            metadata.RecoveryId,
            snapshotPath,
            metadata.OriginalPath,
            metadata.DisplayName,
            metadata.SavedAtUtc,
            metadata.SnapshotLength,
            metadata.SnapshotSha256,
            isStale);

    private async ValueTask<RecoveryMetadata> ReadMetadataAsync(
        string metadataPath,
        string recoveryId,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(metadataPath);

        if (!fileInfo.Exists || fileInfo.Length is 0 or > MaximumMetadataLength)
        {
            throw new InvalidDataException("Recovery metadata is missing or has an invalid size.");
        }

        await using var stream = new FileStream(
            metadataPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var metadata = await JsonSerializer.DeserializeAsync<RecoveryMetadata>(
            stream,
            SerializerOptions,
            cancellationToken);
        ValidateMetadata(metadata, recoveryId);
        return metadata!;
    }

    private static async ValueTask WriteMetadataAsync(
        string stagingPath,
        RecoveryMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            stagingPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, metadata, SerializerOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static void ValidateMetadataFile(string path, string recoveryId)
    {
        var fileInfo = new FileInfo(path);

        if (!fileInfo.Exists || fileInfo.Length is 0 or > MaximumMetadataLength)
        {
            throw new InvalidDataException("Generated recovery metadata has an invalid size.");
        }

        var metadata = JsonSerializer.Deserialize<RecoveryMetadata>(File.ReadAllBytes(path), SerializerOptions);
        ValidateMetadata(metadata, recoveryId);
    }

    private static void ValidateMetadata(RecoveryMetadata? metadata, string recoveryId)
    {
        if (metadata == null
            || metadata.SchemaVersion != CurrentSchemaVersion
            || metadata.RecoveryId != recoveryId
            || string.IsNullOrWhiteSpace(metadata.DisplayName)
            || metadata.DisplayName.Length > 512
            || (metadata.OriginalPath != null && !Path.IsPathFullyQualified(metadata.OriginalPath))
            || metadata.SnapshotLength <= 0
            || metadata.SnapshotSha256.Length != 64
            || !metadata.SnapshotSha256.All(Uri.IsHexDigit)
            || !IsValidSnapshotFileName(metadata.SnapshotFileName))
        {
            throw new InvalidDataException("Recovery metadata is invalid or unsupported.");
        }
    }

    private static async ValueTask ValidateSnapshotAsync(
        string snapshotPath,
        RecoveryMetadata metadata,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(snapshotPath);

        if (!fileInfo.Exists || fileInfo.Length != metadata.SnapshotLength)
        {
            throw new InvalidDataException("The recovery snapshot length does not match its metadata.");
        }

        var actualHash = await ComputeSha256Async(snapshotPath, cancellationToken);

        if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, metadata.SnapshotSha256))
        {
            throw new InvalidDataException("The recovery snapshot hash does not match its metadata.");
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(snapshotPath);
            using var document = SIDocument.Load(stream);
        }, cancellationToken);
    }

    private async ValueTask<bool> IsCanonicalNewerAndValidAsync(
        RecoveryMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.OriginalPath is not { Length: > 0 } originalPath
            || !File.Exists(originalPath)
            || File.GetLastWriteTimeUtc(originalPath) < metadata.SavedAtUtc.UtcDateTime)
        {
            return false;
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(originalPath);
                using var document = SIDocument.Load(stream);
                return true;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "A newer canonical file for recovery {RecoveryId} is invalid; recovery was retained as active",
                metadata.RecoveryId);
            return false;
        }
    }

    private string GetRecoveryDirectory(string recoveryId) =>
        Path.Combine(Path.GetFullPath(_appPaths.RecoveryDirectory), recoveryId);

    private static string ResolveSnapshotPath(string recoveryDirectory, string snapshotFileName)
    {
        var path = Path.GetFullPath(Path.Combine(recoveryDirectory, snapshotFileName));

        if (!IsDirectChild(recoveryDirectory, path))
        {
            throw new InvalidDataException("The recovery snapshot path escapes its recovery directory.");
        }

        return path;
    }

    private static bool IsDirectChild(string directory, string path) => string.Equals(
        Path.GetDirectoryName(path),
        Path.GetFullPath(directory),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsValidSnapshotFileName(string fileName) =>
        fileName == Path.GetFileName(fileName)
        && fileName.StartsWith(SnapshotPrefix, StringComparison.Ordinal)
        && fileName.EndsWith(SnapshotSuffix, StringComparison.Ordinal)
        && fileName.Length > SnapshotPrefix.Length + SnapshotSuffix.Length;

    private static void ValidateRecoveryId(string recoveryId)
    {
        if (!Guid.TryParseExact(recoveryId, "N", out _)
            || recoveryId.Any(character => character is >= 'A' and <= 'F'))
        {
            throw new ArgumentException("Recovery IDs must be 32 lowercase hexadecimal characters.", nameof(recoveryId));
        }
    }

    private static async ValueTask<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private void CommitMetadata(string stagingPath, string metadataPath)
    {
        if (!File.Exists(metadataPath))
        {
            File.Move(stagingPath, metadataPath);
            return;
        }

        var backupPath = $"{metadataPath}.{Guid.NewGuid():N}.bak";

        try
        {
            File.Replace(stagingPath, metadataPath, backupPath, ignoreMetadataErrors: true);
            TryDeleteFile(backupPath, "metadata backup");
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or IOException)
        {
            File.Move(metadataPath, backupPath);

            try
            {
                File.Move(stagingPath, metadataPath);
                TryDeleteFile(backupPath, "metadata fallback backup");
            }
            catch
            {
                if (!File.Exists(metadataPath) && File.Exists(backupPath))
                {
                    File.Move(backupPath, metadataPath);
                }

                throw;
            }
        }
    }

    private void DeleteObsoleteSnapshots(string recoveryDirectory, string currentSnapshotPath)
    {
        foreach (var snapshotPath in Directory.EnumerateFiles(
            recoveryDirectory,
            $"{SnapshotPrefix}*{SnapshotSuffix}",
            SearchOption.TopDirectoryOnly))
        {
            if (snapshotPath == currentSnapshotPath)
            {
                continue;
            }

            try
            {
                File.Delete(snapshotPath);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Obsolete recovery snapshot could not be deleted");
            }
        }
    }

    private void TryDeleteFile(string path, string purpose)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not delete {Purpose} at {Path}; recovery inventory will ignore orphan files",
                purpose,
                path);
        }
    }

    private sealed record RecoveryMetadata(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("recoveryId")] string RecoveryId,
        [property: JsonPropertyName("snapshotFileName")] string SnapshotFileName,
        [property: JsonPropertyName("originalPath")] string? OriginalPath,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("savedAtUtc")] DateTimeOffset SavedAtUtc,
        [property: JsonPropertyName("snapshotLength")] long SnapshotLength,
        [property: JsonPropertyName("snapshotSha256")] string SnapshotSha256);
}
