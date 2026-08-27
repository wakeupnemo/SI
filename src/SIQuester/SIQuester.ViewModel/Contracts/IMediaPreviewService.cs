namespace SIQuester.ViewModel.Contracts;

/// <summary>Identifies media supported by the controlled library preview.</summary>
public enum MediaPreviewKind
{
    /// <summary>Audio rendered with native WebView HTML media controls.</summary>
    Audio,

    /// <summary>Video rendered with native WebView HTML media controls.</summary>
    Video,
}

/// <summary>Describes one package-owned media stream without exposing a UI framework.</summary>
public sealed class MediaPreviewSource
{
    private const int MaxNameLength = 1024;

    /// <summary>Initializes a source whose factory returns a newly owned stream per request.</summary>
    public MediaPreviewSource(MediaPreviewKind kind, string name, Func<QuestionPreviewMediaStream?> openRead)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(openRead);
        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException("The media-preview name exceeds the allowed length.", nameof(name));
        }

        Kind = kind;
        Name = name;
        OpenRead = openRead;
    }

    /// <summary>Gets the media kind.</summary>
    public MediaPreviewKind Kind { get; }

    /// <summary>Gets the canonical package media name.</summary>
    public string Name { get; }

    /// <summary>Gets a factory that opens a newly owned stream.</summary>
    public Func<QuestionPreviewMediaStream?> OpenRead { get; }
}

/// <summary>Owns one controlled audio/video playback registration.</summary>
public interface IMediaPreviewSession : IDisposable
{
    /// <summary>Gets the application-owned playback page, or null when unavailable.</summary>
    Uri? Source { get; }

    /// <summary>Gets why playback is unavailable.</summary>
    QuestionPreviewAvailability Availability { get; }

    /// <summary>Gets an actionable native backend requirement.</summary>
    QuestionPreviewBackendRequirement BackendRequirement { get; }

    /// <summary>Gets whether the playback page can be hosted.</summary>
    bool IsAvailable => Availability == QuestionPreviewAvailability.Available && Source is { IsAbsoluteUri: true };
}

/// <summary>Creates bounded, disposable sessions for package audio and video.</summary>
public interface IMediaPreviewService
{
    /// <summary>Creates a session for one selected media item.</summary>
    IMediaPreviewSession CreateSession(MediaPreviewSource source);
}
