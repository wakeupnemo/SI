using SIQuester.ViewModel.Contracts.Host;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SIQuester.Services.Host;

/// <inheritdoc />
internal sealed class ClipboardService : IClipboardService
{
    public ValueTask WriteAsync(ClipboardWriteRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var dataObject = new DataObject();

        if (request.Text != null)
        {
            dataObject.SetText(request.Text, TextDataFormat.UnicodeText);
        }

        if (request.FilePaths.Count > 0)
        {
            var paths = new StringCollection();
            paths.AddRange(request.FilePaths.ToArray());
            dataObject.SetFileDropList(paths);
        }

        if (request.ImagePng is { Length: > 0 } imagePng)
        {
            using var stream = new MemoryStream(imagePng, writable: false);
            var decoder = new PngBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            dataObject.SetImage(decoder.Frames[0]);
        }

        foreach (var customData in request.CustomData)
        {
            object value = customData.Format.Kind == ClipboardCustomDataKind.Utf8Text
                ? Encoding.UTF8.GetString(customData.Data)
                : customData.Data.ToArray();
            dataObject.SetData(customData.Format.Name, value);
        }

        Clipboard.SetDataObject(dataObject, copy: true);
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Clipboard.ContainsText(TextDataFormat.UnicodeText)
            ? Clipboard.GetText(TextDataFormat.UnicodeText)
            : null);
    }

    public ValueTask<IReadOnlyList<string>> ReadFilePathsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> paths = Clipboard.ContainsFileDropList()
            ? Clipboard.GetFileDropList().Cast<string>().ToArray()
            : Array.Empty<string>();
        return ValueTask.FromResult(paths);
    }

    public ValueTask<byte[]?> ReadImagePngAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Clipboard.ContainsImage())
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        var bitmap = Clipboard.GetImage();

        if (bitmap == null)
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return ValueTask.FromResult<byte[]?>(stream.ToArray());
    }

    public ValueTask<byte[]?> ReadCustomDataAsync(
        ClipboardCustomFormat format,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Clipboard.ContainsData(format.Name))
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        var value = Clipboard.GetData(format.Name);
        var data = value switch
        {
            byte[] bytes => bytes.ToArray(),
            string text => Encoding.UTF8.GetBytes(text),
            MemoryStream stream => stream.ToArray(),
            _ => null,
        };
        return ValueTask.FromResult(data);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clipboard.Clear();
        return ValueTask.CompletedTask;
    }
}
