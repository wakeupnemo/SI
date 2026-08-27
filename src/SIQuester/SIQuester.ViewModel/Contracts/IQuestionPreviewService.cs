namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Describes whether the host can present the application-owned question player.
/// </summary>
public enum QuestionPreviewAvailability
{
    /// <summary>The host and player assets are ready.</summary>
    Available,

    /// <summary>The native browser backend is unavailable or has not been configured.</summary>
    BackendUnavailable,

    /// <summary>The application-owned player assets are unavailable.</summary>
    AssetsUnavailable,
}

/// <summary>
/// Identifies an actionable native runtime requirement without embedding localized host text in the view model.
/// </summary>
public enum QuestionPreviewBackendRequirement
{
    /// <summary>No platform-specific installation guidance is available.</summary>
    None,

    /// <summary>Linux requires WPE WebKit or GTK 3 with WebKitGTK.</summary>
    LinuxWebKit,

    /// <summary>Windows requires the Microsoft Edge WebView2 runtime.</summary>
    WindowsWebView2,

    /// <summary>The current operating-system platform is not supported.</summary>
    UnsupportedPlatform,
}

/// <summary>Identifies media that the application-owned player may request from a preview session.</summary>
public enum QuestionPreviewMediaKind
{
    /// <summary>Image content rendered by the retained player.</summary>
    Image,

    /// <summary>Audio content rendered by the retained player.</summary>
    Audio,

    /// <summary>Video content rendered by the retained player.</summary>
    Video,
}

/// <summary>Owns one readable package-media stream opened for a preview request.</summary>
public sealed class QuestionPreviewMediaStream : IDisposable
{
    /// <summary>Initializes a readable stream with its authoritative uncompressed length.</summary>
    public QuestionPreviewMediaStream(Stream stream, long length)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("Question-preview media must be readable.", nameof(stream));
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Stream = stream;
        Length = length;
    }

    /// <summary>Gets the owned readable stream.</summary>
    public Stream Stream { get; }

    /// <summary>Gets the authoritative uncompressed length.</summary>
    public long Length { get; }

    /// <inheritdoc />
    public void Dispose() => Stream.Dispose();
}

/// <summary>
/// Describes one package-owned media source without exposing package containers or UI-framework objects.
/// </summary>
public sealed class QuestionPreviewMediaSource
{
    private const int MaxNameLength = 1024;

    /// <summary>Initializes a package-media source whose factory returns a newly owned stream per request.</summary>
    public QuestionPreviewMediaSource(
        QuestionPreviewMediaKind kind,
        string name,
        Func<QuestionPreviewMediaStream?> openRead)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(openRead);

        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException("The question-preview media name exceeds the allowed length.", nameof(name));
        }

        Kind = kind;
        Name = name;
        OpenRead = openRead;
    }

    /// <summary>Gets the semantic player media kind.</summary>
    public QuestionPreviewMediaKind Kind { get; }

    /// <summary>Gets the canonical package media name.</summary>
    public string Name { get; }

    /// <summary>Gets a factory that opens a new owned stream for each request.</summary>
    public Func<QuestionPreviewMediaStream?> OpenRead { get; }
}

/// <summary>
/// Describes the current question-preview host without exposing UI-framework objects.
/// </summary>
public sealed record QuestionPreviewHostDescriptor
{
    private QuestionPreviewHostDescriptor(
        QuestionPreviewAvailability availability,
        Uri? applicationSource,
        QuestionPreviewBackendRequirement backendRequirement)
    {
        Availability = availability;
        ApplicationSource = applicationSource;
        BackendRequirement = backendRequirement;
    }

    /// <summary>Gets the current availability state.</summary>
    public QuestionPreviewAvailability Availability { get; }

    /// <summary>Gets the application-owned player source when available.</summary>
    public Uri? ApplicationSource { get; }

    /// <summary>Gets platform-specific installation guidance for an unavailable backend.</summary>
    public QuestionPreviewBackendRequirement BackendRequirement { get; }

    /// <summary>Gets whether preview can be started.</summary>
    public bool IsAvailable => Availability == QuestionPreviewAvailability.Available
        && ApplicationSource is { IsAbsoluteUri: true };

    /// <summary>Creates an available descriptor for an absolute application-owned source.</summary>
    public static QuestionPreviewHostDescriptor Available(Uri applicationSource)
    {
        ArgumentNullException.ThrowIfNull(applicationSource);

        if (!applicationSource.IsAbsoluteUri)
        {
            throw new ArgumentException("The question-preview source must be absolute.", nameof(applicationSource));
        }

        var isFile = string.Equals(applicationSource.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase);
        var isLoopbackHttp = applicationSource.IsLoopback
            && (string.Equals(applicationSource.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(applicationSource.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

        if (!isFile && !isLoopbackHttp)
        {
            throw new ArgumentException(
                "The question-preview source must use a local file or loopback HTTP origin.",
                nameof(applicationSource));
        }

        return new QuestionPreviewHostDescriptor(
            QuestionPreviewAvailability.Available,
            applicationSource,
            QuestionPreviewBackendRequirement.None);
    }

    /// <summary>Creates an unavailable descriptor without exposing a source that must not be loaded.</summary>
    public static QuestionPreviewHostDescriptor Unavailable(
        QuestionPreviewAvailability availability,
        QuestionPreviewBackendRequirement backendRequirement = QuestionPreviewBackendRequirement.None)
    {
        if (availability == QuestionPreviewAvailability.Available)
        {
            throw new ArgumentOutOfRangeException(nameof(availability));
        }

        if (availability != QuestionPreviewAvailability.BackendUnavailable
            && backendRequirement != QuestionPreviewBackendRequirement.None)
        {
            throw new ArgumentException(
                "Backend installation guidance is only valid for an unavailable backend.",
                nameof(backendRequirement));
        }

        return new QuestionPreviewHostDescriptor(availability, null, backendRequirement);
    }
}

/// <summary>Owns the host and package-media registrations for one question-preview dialog.</summary>
public interface IQuestionPreviewSession : IDisposable
{
    /// <summary>Gets the native host capability captured for this session.</summary>
    QuestionPreviewHostDescriptor Host { get; }

    /// <summary>
    /// Resolves package-owned media to a host-controlled player source.
    /// Returns false when the host cannot safely expose the media.
    /// </summary>
    bool TryGetMediaSource(QuestionPreviewMediaSource media, out string source);
}

/// <summary>Provides host capability without exposing package media.</summary>
public sealed class QuestionPreviewHostSession : IQuestionPreviewSession
{
    /// <summary>Initializes a host-only preview session.</summary>
    public QuestionPreviewHostSession(QuestionPreviewHostDescriptor host) =>
        Host = host ?? throw new ArgumentNullException(nameof(host));

    /// <inheritdoc />
    public QuestionPreviewHostDescriptor Host { get; }

    /// <inheritdoc />
    public bool TryGetMediaSource(QuestionPreviewMediaSource media, out string source)
    {
        ArgumentNullException.ThrowIfNull(media);
        source = string.Empty;
        return false;
    }

    /// <inheritdoc />
    public void Dispose() { }
}

/// <summary>
/// Resolves host-owned question-preview capability and application source.
/// </summary>
public interface IQuestionPreviewService
{
    /// <summary>Gets the current host descriptor.</summary>
    QuestionPreviewHostDescriptor GetHostDescriptor();

    /// <summary>
    /// Creates one owned dialog session. Hosts that do not expose package media retain the safe host-only behavior.
    /// </summary>
    IQuestionPreviewSession CreateSession() => new QuestionPreviewHostSession(GetHostDescriptor());
}
