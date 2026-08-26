namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Describes a file type without exposing a UI framework type.
/// </summary>
/// <param name="Name">Localized display name.</param>
/// <param name="Extensions">Extensions without a leading dot.</param>
public sealed record FileTypeFilter(string Name, IReadOnlyList<string> Extensions);

/// <summary>
/// Describes an open-file request.
/// </summary>
public sealed record OpenFilePickerRequest(
    string? Title,
    IReadOnlyList<FileTypeFilter> FileTypes,
    bool AllowMultiple = true);

/// <summary>
/// Describes a save-file request.
/// </summary>
public sealed record SaveFilePickerRequest(
    string? Title,
    string SuggestedFileName,
    string DefaultExtension,
    IReadOnlyList<FileTypeFilter> FileTypes);

/// <summary>
/// Represents a selected file with stable, platform-neutral access semantics.
/// </summary>
public sealed class PickedFile
{
    private readonly Func<CancellationToken, ValueTask<Stream>> _openReadAsync;
    private readonly Func<CancellationToken, ValueTask<Stream>>? _openWriteAsync;

    public string? LocalPath { get; }

    public string DisplayName { get; }

    public string Extension { get; }

    public PickedFile(
        string? localPath,
        string displayName,
        string extension,
        Func<CancellationToken, ValueTask<Stream>> openReadAsync,
        Func<CancellationToken, ValueTask<Stream>>? openWriteAsync = null)
    {
        LocalPath = localPath;
        DisplayName = displayName;
        Extension = extension;
        _openReadAsync = openReadAsync;
        _openWriteAsync = openWriteAsync;
    }

    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        _openReadAsync(cancellationToken);

    public ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        _openWriteAsync?.Invoke(cancellationToken)
        ?? ValueTask.FromException<Stream>(new NotSupportedException("The selected file is read-only."));

    public static PickedFile FromLocalPath(string path, bool writable = false)
    {
        var fullPath = Path.GetFullPath(path);

        return new PickedFile(
            fullPath,
            Path.GetFileName(fullPath),
            Path.GetExtension(fullPath),
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<Stream>(File.OpenRead(fullPath));
            },
            writable
                ? cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult<Stream>(new FileStream(
                        fullPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        81920,
                        FileOptions.Asynchronous));
                }
                : null);
    }
}

/// <summary>
/// Selects files without leaking framework-specific storage objects.
/// </summary>
public interface IFilePickerService
{
    ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
        OpenFilePickerRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PickedFile?> PickSaveFileAsync(
        SaveFilePickerRequest request,
        CancellationToken cancellationToken = default);
}
