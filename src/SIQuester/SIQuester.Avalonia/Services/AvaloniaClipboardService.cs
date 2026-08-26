using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SIQuester.ViewModel.Contracts.Host;
using System.Text;

namespace SIQuester.Avalonia.Services;

/// <summary>
/// Maps platform-neutral clipboard values to Avalonia 12 data-transfer formats.
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    private static readonly DataFormat<byte[]> OriginalPngFormat =
        DataFormat.CreateBytesApplicationFormat("SIQuester.ImagePng.v1");

    private readonly Func<TopLevel?> _getTopLevel;

    public AvaloniaClipboardService(Func<TopLevel?> getTopLevel) =>
        _getTopLevel = getTopLevel ?? throw new ArgumentNullException(nameof(getTopLevel));

    public async ValueTask WriteAsync(
        ClipboardWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var topLevel = GetTopLevel();
        var clipboard = GetClipboard(topLevel);
        var transfer = new DataTransfer();
        var transferred = false;

        try
        {
            var mainItem = new DataTransferItem();

            if (request.Text != null)
            {
                mainItem.SetText(request.Text);
            }

            foreach (var customData in request.CustomData)
            {
                if (customData.Format.Kind == ClipboardCustomDataKind.Utf8Text)
                {
                    mainItem.Set(
                        CreateStringFormat(customData.Format),
                        Encoding.UTF8.GetString(customData.Data));
                }
                else
                {
                    mainItem.Set(CreateBytesFormat(customData.Format), customData.Data.ToArray());
                }
            }

            if (request.ImagePng is { Length: > 0 } imagePng)
            {
                using var stream = new MemoryStream(imagePng, writable: false);
                mainItem.Set(OriginalPngFormat, imagePng.ToArray());
                mainItem.SetBitmap(new Bitmap(stream));
            }

            if (mainItem.Formats.Count > 0)
            {
                transfer.Add(mainItem);
            }

            foreach (var filePath in request.FilePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IStorageItem? storageItem = File.Exists(filePath)
                    ? await topLevel.StorageProvider.TryGetFileFromPathAsync(filePath)
                    : Directory.Exists(filePath)
                        ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(filePath)
                        : null;

                if (storageItem != null)
                {
                    transfer.Add(DataTransferItem.CreateFile(storageItem));
                }
                else
                {
                    throw new FileNotFoundException("A clipboard file no longer exists.", filePath);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (transfer.Items.Count == 0)
            {
                await clipboard.ClearAsync();
                return;
            }

            await clipboard.SetDataAsync(transfer);
            transferred = true;
            await clipboard.FlushAsync();
        }
        finally
        {
            if (!transferred)
            {
                ((IDisposable)transfer).Dispose();
            }
        }
    }

    public async ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var transfer = await GetClipboard(GetTopLevel()).TryGetDataAsync();
        return transfer == null ? null : await transfer.TryGetTextAsync();
    }

    public async ValueTask<IReadOnlyList<string>> ReadFilePathsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var transfer = await GetClipboard(GetTopLevel()).TryGetDataAsync();

        if (transfer == null)
        {
            return Array.Empty<string>();
        }

        var files = await transfer.TryGetFilesAsync();
        return files?
            .Select(StorageProviderExtensions.TryGetLocalPath)
            .Where(path => path != null)
            .Cast<string>()
            .ToArray()
            ?? Array.Empty<string>();
    }

    public async ValueTask<byte[]?> ReadImagePngAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var transfer = await GetClipboard(GetTopLevel()).TryGetDataAsync();

        if (transfer == null)
        {
            return null;
        }

        var originalPng = await transfer.TryGetValueAsync(OriginalPngFormat);

        if (originalPng is { Length: > 0 })
        {
            return originalPng.ToArray();
        }

        var bitmap = await transfer.TryGetBitmapAsync();

        if (bitmap == null)
        {
            return null;
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.Length == 0 ? null : stream.ToArray();
    }

    public async ValueTask<byte[]?> ReadCustomDataAsync(
        ClipboardCustomFormat format,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var transfer = await GetClipboard(GetTopLevel()).TryGetDataAsync();

        if (transfer == null)
        {
            return null;
        }

        if (format.Kind == ClipboardCustomDataKind.Utf8Text)
        {
            var text = await transfer.TryGetValueAsync(CreateStringFormat(format));
            return text == null ? null : Encoding.UTF8.GetBytes(text);
        }

        var value = await transfer.TryGetValueAsync(CreateBytesFormat(format));
        return value?.ToArray();
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await GetClipboard(GetTopLevel()).ClearAsync();
    }

    private TopLevel GetTopLevel() => _getTopLevel()
        ?? throw new InvalidOperationException("Clipboard access requires an initialized desktop window.");

    private static IClipboard GetClipboard(TopLevel topLevel) => topLevel.Clipboard
        ?? throw new InvalidOperationException("The current desktop backend does not provide a clipboard.");

    private static DataFormat<string> CreateStringFormat(ClipboardCustomFormat format) =>
        format.Scope == ClipboardCustomDataScope.Application
            ? DataFormat.CreateStringApplicationFormat(format.Name)
            : DataFormat.CreateStringPlatformFormat(format.Name);

    private static DataFormat<byte[]> CreateBytesFormat(ClipboardCustomFormat format) =>
        format.Scope == ClipboardCustomDataScope.Application
            ? DataFormat.CreateBytesApplicationFormat(format.Name)
            : DataFormat.CreateBytesPlatformFormat(format.Name);
}
