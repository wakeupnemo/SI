using Microsoft.Extensions.Logging;
using SIPackages;
using SIPackages.Containers;
using SIQuester.ViewModel.Contracts;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Saves SIQ files through a validated same-directory temporary file.
/// </summary>
internal sealed class SafeDocumentPersistenceService : IDocumentPersistenceService
{
    private readonly ILogger<SafeDocumentPersistenceService> _logger;
    private readonly Action<string> _validateDocument;

    public SafeDocumentPersistenceService(ILoggerFactory loggerFactory) =>
        (_logger, _validateDocument) =
            (loggerFactory.CreateLogger<SafeDocumentPersistenceService>(), ValidateDocument);

    internal SafeDocumentPersistenceService(
        ILoggerFactory loggerFactory,
        Action<string> validateDocument) =>
        (_logger, _validateDocument) =
            (loggerFactory.CreateLogger<SafeDocumentPersistenceService>(), validateDocument);

    public async ValueTask SaveAsync(
        QDocument document,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new InvalidOperationException("The destination directory could not be resolved.");

        if (!Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException(destinationDirectory);
        }

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");

        CommitResult commitResult = default;
        var sourceDetached = false;
        var sourceReloaded = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DocumentSnapshotWriter.WriteNewAsync(document, temporaryPath, cancellationToken);
            _validateDocument(temporaryPath);

            cancellationToken.ThrowIfCancellationRequested();

            // SIDocument keeps the source stream open. Release it only after a complete,
            // validated replacement has been produced, then always reload a valid source.
            document.Document.UpdateContainer(EmptySIPackageContainer.Instance);
            sourceDetached = true;

            try
            {
                commitResult = CommitTemporaryFile(temporaryPath, fullDestinationPath);
                _validateDocument(fullDestinationPath);

                var newStream = File.OpenRead(fullDestinationPath);

                try
                {
                    document.Document.ResetTo(newStream);
                    sourceReloaded = true;
                }
                catch
                {
                    newStream.Dispose();
                    throw;
                }

                document.AcceptPendingMediaChanges();
            }
            catch
            {
                if (commitResult.IsCommitted)
                {
                    document.Document.UpdateContainer(EmptySIPackageContainer.Instance);
                    RestorePreviousDestination(fullDestinationPath, commitResult.BackupPath);
                    sourceReloaded = false;
                }

                throw;
            }

            _logger.LogInformation(
                "Document was validated and committed to {DestinationPath}",
                fullDestinationPath);

            if (commitResult.BackupPath != null && commitResult.RetainBackupOnSuccess)
            {
                _logger.LogWarning(
                    "Atomic replacement was unavailable. Recovery backup retained at {BackupPath}",
                    commitResult.BackupPath);
            }
            else if (commitResult.BackupPath != null)
            {
                TryDeleteFile(commitResult.BackupPath, "completed atomic-save backup");
            }
        }
        finally
        {
            if (sourceDetached && !sourceReloaded)
            {
                ReloadBestAvailableSource(document, fullDestinationPath, commitResult.BackupPath);
            }

            TryDeleteFile(temporaryPath, "temporary save file");
        }
    }

    private static void ValidateDocument(string path)
    {
        using var validationStream = File.OpenRead(path);
        using var validationDocument = SIDocument.Load(validationStream);
    }

    private static CommitResult CommitTemporaryFile(string temporaryPath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            File.Move(temporaryPath, destinationPath);
            return new CommitResult(null, RetainBackupOnSuccess: false, IsCommitted: true);
        }

        var backupPath = GetUniqueBackupPath(destinationPath);

        try
        {
            File.Replace(temporaryPath, destinationPath, backupPath, ignoreMetadataErrors: true);
            return new CommitResult(backupPath, RetainBackupOnSuccess: false, IsCommitted: true);
        }
        catch (PlatformNotSupportedException)
        {
            CommitWithRecoverableBackup(temporaryPath, destinationPath, backupPath);
            return new CommitResult(backupPath, RetainBackupOnSuccess: true, IsCommitted: true);
        }
    }

    private void RestorePreviousDestination(string destinationPath, string? backupPath)
    {
        if (backupPath == null || !File.Exists(backupPath))
        {
            return;
        }

        try
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(backupPath, destinationPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(backupPath, destinationPath);
            }

            _logger.LogWarning("Previous document restored after failed save at {DestinationPath}", destinationPath);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Automatic save rollback failed. Previous document remains at {BackupPath}",
                backupPath);
        }
    }

    private static void CommitWithRecoverableBackup(
        string temporaryPath,
        string destinationPath,
        string backupPath)
    {
        File.Move(destinationPath, backupPath);

        try
        {
            File.Move(temporaryPath, destinationPath);
        }
        catch
        {
            if (!File.Exists(destinationPath) && File.Exists(backupPath))
            {
                File.Move(backupPath, destinationPath);
            }

            throw;
        }
    }

    private static string GetUniqueBackupPath(string destinationPath)
    {
        var basePath = $"{destinationPath}.bak";

        for (var index = 0; ; index++)
        {
            var candidate = index == 0 ? basePath : $"{basePath}.{index}";

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private void ReloadBestAvailableSource(QDocument document, string destinationPath, string? backupPath)
    {
        var sourcePath = backupPath != null && IsValidDocument(backupPath)
            ? backupPath
            : IsValidDocument(destinationPath)
                ? destinationPath
                : null;

        if (sourcePath == null)
        {
            _logger.LogError("Document source could not be reloaded after a failed save");
            return;
        }

        try
        {
            document.Document.ResetTo(File.OpenRead(sourcePath));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Document source reload failed for {SourcePath}", sourcePath);
        }
    }

    private bool IsValidDocument(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            _validateDocument(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TryDeleteFile(string path, string purpose)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not remove {Purpose} {Path}", purpose, path);
        }
    }

    private readonly record struct CommitResult(
        string? BackupPath,
        bool RetainBackupOnSuccess,
        bool IsCommitted);
}
