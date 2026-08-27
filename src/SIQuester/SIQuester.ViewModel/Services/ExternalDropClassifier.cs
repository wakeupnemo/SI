using SIPackages;
using SIPackages.Core;
using SIPackages.Models;
using SIQuester.ViewModel.Contracts;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Identifies the supported role of a file supplied by an external drag operation.
/// </summary>
public enum ExternalDropFileKind
{
    Unsupported,
    Package,
    Text,
    Image,
    Audio,
    Video,
    Html,
}

/// <summary>
/// Describes a framework-neutral external file without opening it.
/// </summary>
public readonly record struct ExternalDropFileClassification(
    ExternalDropFileKind Kind,
    string? CollectionName,
    string? ContentType)
{
    public bool IsSupported => Kind != ExternalDropFileKind.Unsupported;

    public bool IsMedia => CollectionName != null && ContentType != null;
}

/// <summary>
/// Classifies files supported by the flat editor's external-drop path.
/// </summary>
public static class ExternalDropClassifier
{
    public static ExternalDropFileClassification Classify(PickedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Classify(file.DisplayName, file.Extension);
    }

    public static ExternalDropFileClassification Classify(string displayName, string? declaredExtension = null)
    {
        var extension = NormalizeExtension(displayName, declaredExtension);

        if (extension == ".siq")
        {
            return new ExternalDropFileClassification(ExternalDropFileKind.Package, null, null);
        }

        if (extension == ".txt")
        {
            return new ExternalDropFileClassification(ExternalDropFileKind.Text, null, null);
        }

        foreach (var mediaExtensions in Quality.FileExtensions)
        {
            if (!mediaExtensions.Value.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var contentType = CollectionNames.TryGetContentType(mediaExtensions.Key);
            return new ExternalDropFileClassification(
                ToMediaKind(contentType),
                mediaExtensions.Key,
                contentType);
        }

        return new ExternalDropFileClassification(ExternalDropFileKind.Unsupported, null, null);
    }

    private static string NormalizeExtension(string displayName, string? declaredExtension)
    {
        var extension = Path.GetExtension(displayName);

        if (extension.Length == 0 && !string.IsNullOrWhiteSpace(declaredExtension))
        {
            extension = declaredExtension.Trim();
        }

        if (extension.Length > 0 && extension[0] != '.')
        {
            extension = $".{extension}";
        }

        return extension.ToLowerInvariant();
    }

    private static ExternalDropFileKind ToMediaKind(string? contentType) => contentType switch
    {
        ContentTypes.Image => ExternalDropFileKind.Image,
        ContentTypes.Audio => ExternalDropFileKind.Audio,
        ContentTypes.Video => ExternalDropFileKind.Video,
        ContentTypes.Html => ExternalDropFileKind.Html,
        _ => ExternalDropFileKind.Unsupported,
    };
}

/// <summary>
/// Reports the outcome of importing one external file into an editor workspace.
/// </summary>
public enum ExternalFileImportResult
{
    Imported,
    Unsupported,
    QuestionTargetRequired,
    HostUnavailable,
    Failed,
}
