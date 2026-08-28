using SIPackages;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Writes a complete package snapshot without changing the live document container.
/// </summary>
internal static class DocumentSnapshotWriter
{
    internal static async ValueTask WriteNewAsync(
        QDocument document,
        string destinationPath,
        CancellationToken cancellationToken,
        bool validateSnapshot = true)
    {
        await using (var stream = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            SIDocument? temporaryDocument = null;

            try
            {
                temporaryDocument = await document.Document.SaveAsAsync(stream, false, cancellationToken);
                await document.ApplyPendingMediaChangesAsync(temporaryDocument, cancellationToken);
            }
            finally
            {
                if (temporaryDocument != null)
                {
                    await Task.Run(temporaryDocument.Dispose);
                }
            }
        }

        // Disposing the package finalizes the ZIP. Reopen and issue an explicit durable flush
        // away from the UI thread because fsync latency is controlled by the destination device.
        await Task.Run(() =>
        {
            using var flushStream = new FileStream(
                destinationPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read,
                1,
                FileOptions.WriteThrough);
            flushStream.Flush(flushToDisk: true);
        }, cancellationToken);

        if (!validateSnapshot)
        {
            return;
        }

        await Task.Run(() =>
        {
            using var validationStream = File.OpenRead(destinationPath);
            using var validationDocument = SIDocument.Load(validationStream);
        }, cancellationToken);
    }
}
