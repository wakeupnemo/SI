using Avalonia.Media.Imaging;
using SIPackages.Core;

namespace SIQuester.Avalonia.Helpers;

internal static class BoundedBitmapLoader
{
    private const long MaxEncodedImageBytes = 32L * 1024 * 1024;
    private const int MaxImageDimension = 16_384;
    private const long MaxImagePixels = 100_000_000;

    internal static async Task<Bitmap> LoadAsync(
        StreamInfo? streamInfo,
        CancellationToken cancellationToken = default)
    {
        if (streamInfo == null || streamInfo.Length < 0 || streamInfo.Length > MaxEncodedImageBytes)
        {
            throw new InvalidDataException("The image is unavailable or exceeds the encoded-size limit.");
        }

        await using var source = streamInfo.Stream;
        using var content = new MemoryStream((int)streamInfo.Length);
        var buffer = new byte[81920];

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (content.Length + read > MaxEncodedImageBytes)
            {
                throw new InvalidDataException("The image exceeds the encoded-size limit.");
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var bytes = content.ToArray();
        var bitmap = await Task.Run(() =>
        {
            using var bitmapStream = new MemoryStream(bytes, writable: false);
            return new Bitmap(bitmapStream);
        }, cancellationToken);

        if (bitmap.PixelSize.Width > MaxImageDimension
            || bitmap.PixelSize.Height > MaxImageDimension
            || (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > MaxImagePixels)
        {
            bitmap.Dispose();
            throw new InvalidDataException("The decoded image exceeds the dimension limit.");
        }

        return bitmap;
    }
}
