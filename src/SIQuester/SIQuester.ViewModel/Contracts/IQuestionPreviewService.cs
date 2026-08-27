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
/// Describes the current question-preview host without exposing UI-framework objects.
/// </summary>
public sealed record QuestionPreviewHostDescriptor
{
    private QuestionPreviewHostDescriptor(
        QuestionPreviewAvailability availability,
        Uri? applicationSource)
    {
        Availability = availability;
        ApplicationSource = applicationSource;
    }

    /// <summary>Gets the current availability state.</summary>
    public QuestionPreviewAvailability Availability { get; }

    /// <summary>Gets the application-owned player source when available.</summary>
    public Uri? ApplicationSource { get; }

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

        return new QuestionPreviewHostDescriptor(QuestionPreviewAvailability.Available, applicationSource);
    }

    /// <summary>Creates an unavailable descriptor without exposing a source that must not be loaded.</summary>
    public static QuestionPreviewHostDescriptor Unavailable(QuestionPreviewAvailability availability)
    {
        if (availability == QuestionPreviewAvailability.Available)
        {
            throw new ArgumentOutOfRangeException(nameof(availability));
        }

        return new QuestionPreviewHostDescriptor(availability, null);
    }
}

/// <summary>
/// Resolves host-owned question-preview capability and application source.
/// </summary>
public interface IQuestionPreviewService
{
    /// <summary>Gets the current host descriptor.</summary>
    QuestionPreviewHostDescriptor GetHostDescriptor();
}
