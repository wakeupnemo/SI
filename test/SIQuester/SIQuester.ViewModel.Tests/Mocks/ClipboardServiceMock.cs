using SIQuester.ViewModel.Contracts.Host;

namespace SIQuester.ViewModel.Tests.Mocks;

/// <summary>
/// Mock implementation of clipboard service for testing.
/// </summary>
internal sealed class ClipboardServiceMock : IClipboardService
{
    private readonly Dictionary<ClipboardCustomFormat, byte[]> _customData = new();
    private string? _text;
    private IReadOnlyList<string> _filePaths = Array.Empty<string>();
    private byte[]? _imagePng;

    public Exception? WriteException { get; set; }

    public TaskCompletionSource? WriteStored { get; set; }

    public Task? WriteReturnRelease { get; set; }

    public async ValueTask WriteAsync(ClipboardWriteRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (WriteException != null)
        {
            throw WriteException;
        }

        _text = request.Text;
        _filePaths = request.FilePaths.ToArray();
        _imagePng = request.ImagePng?.ToArray();
        _customData.Clear();

        foreach (var item in request.CustomData)
        {
            _customData[item.Format] = item.Data.ToArray();
        }

        WriteStored?.TrySetResult();

        if (WriteReturnRelease != null)
        {
            // Deliberately allow a test to close the source after the clipboard has accepted
            // the payload but before the caller resumes from WriteAsync.
            await WriteReturnRelease;
        }

    }

    public ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_text);
    }

    public ValueTask<IReadOnlyList<string>> ReadFilePathsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_filePaths);
    }

    public ValueTask<byte[]?> ReadImagePngAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_imagePng?.ToArray());
    }

    public ValueTask<byte[]?> ReadCustomDataAsync(
        ClipboardCustomFormat format,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_customData.TryGetValue(format, out var data) ? data.ToArray() : null);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _text = null;
        _filePaths = Array.Empty<string>();
        _imagePng = null;
        _customData.Clear();
        return ValueTask.CompletedTask;
    }

    public void Clear() => ClearAsync().GetAwaiter().GetResult();
}
