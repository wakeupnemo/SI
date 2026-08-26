namespace SIQuester.ViewModel.Contracts.Host;

/// <summary>
/// Defines the value representation of a custom clipboard format.
/// </summary>
public enum ClipboardCustomDataKind
{
    Binary,
    Utf8Text,
}

/// <summary>
/// Defines whether a custom format is mapped by the UI framework or passed directly to the platform.
/// </summary>
public enum ClipboardCustomDataScope
{
    Application,
    Platform,
}

/// <summary>
/// Identifies one typed custom clipboard format.
/// </summary>
public sealed record ClipboardCustomFormat(
    string Name,
    ClipboardCustomDataKind Kind,
    ClipboardCustomDataScope Scope = ClipboardCustomDataScope.Application);

/// <summary>
/// Contains one custom clipboard value.
/// </summary>
public sealed record ClipboardCustomData(ClipboardCustomFormat Format, byte[] Data)
{
    public static ClipboardCustomData FromText(ClipboardCustomFormat format, string value) =>
        new(format, System.Text.Encoding.UTF8.GetBytes(value));
}

/// <summary>
/// Defines a multi-format clipboard write without exposing UI-framework objects.
/// </summary>
public sealed class ClipboardWriteRequest
{
    public string? Text { get; init; }

    public IReadOnlyList<string> FilePaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets PNG-encoded image bytes.
    /// </summary>
    public byte[]? ImagePng { get; init; }

    public IReadOnlyList<ClipboardCustomData> CustomData { get; init; } = Array.Empty<ClipboardCustomData>();
}

/// <summary>
/// Provides asynchronous typed access to the system clipboard.
/// </summary>
public interface IClipboardService
{
    ValueTask WriteAsync(ClipboardWriteRequest request, CancellationToken cancellationToken = default);

    ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<string>> ReadFilePathsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an image and returns PNG-encoded bytes, or <see langword="null" /> when unavailable.
    /// </summary>
    ValueTask<byte[]?> ReadImagePngAsync(CancellationToken cancellationToken = default);

    ValueTask<byte[]?> ReadCustomDataAsync(
        ClipboardCustomFormat format,
        CancellationToken cancellationToken = default);

    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}
