using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;

namespace SIQuester.ViewModel;

/// <summary>
/// Defines a sidebar media object view model.
/// </summary>
public sealed class MediaItemViewModel : MediaOwnerViewModel
{
    private readonly Func<StreamInfo?> _streamGetter;
    private readonly Func<CancellationToken, ValueTask<StreamInfo?>> _streamGetterAsync;
    private readonly IMediaPreviewService _mediaPreviewService;

    /// <summary>
    /// Media item type.
    /// </summary>
    public override string Type { get; }

    /// <summary>
    /// Underlying media object.
    /// </summary>
    public Named Model { get; }

    public string Name => Model.Name;

    /// <summary>
    /// Gets whether this item belongs to the package image collection.
    /// </summary>
    public bool IsImage => Type == CollectionNames.ImagesStorageName;

    /// <summary>Gets whether this item can use the controlled audio/video player.</summary>
    public bool IsPlayable => Type is CollectionNames.AudioStorageName or CollectionNames.VideoStorageName;

    private readonly Func<IMedia> _mediaGetter;

    public MediaItemViewModel(
        Named named,
        string type,
        Func<IMedia> mediaGetter,
        Func<StreamInfo?> streamGetter,
        Func<CancellationToken, ValueTask<StreamInfo?>> streamGetterAsync,
        IMediaPreviewService mediaPreviewService)
    {
        Model = named;
        Type = type;
        _mediaGetter = mediaGetter;
        _streamGetter = streamGetter;
        _streamGetterAsync = streamGetterAsync;
        _mediaPreviewService = mediaPreviewService;
    }

    /// <summary>
    /// Opens the original package bytes without invoking a platform media backend.
    /// </summary>
    public StreamInfo? OpenStream() => _streamGetter();

    /// <summary>
    /// Opens the original package bytes without synchronously waiting for document persistence.
    /// </summary>
    public ValueTask<StreamInfo?> OpenStreamAsync(CancellationToken cancellationToken = default) =>
        _streamGetterAsync(cancellationToken);

    /// <summary>Creates an owned controlled playback session for this item.</summary>
    public IMediaPreviewSession CreatePreviewSession()
    {
        if (!IsPlayable)
        {
            throw new InvalidOperationException("Only package audio and video can be played.");
        }

        var kind = Type == CollectionNames.AudioStorageName ? MediaPreviewKind.Audio : MediaPreviewKind.Video;
        return _mediaPreviewService.CreateSession(new MediaPreviewSource(
            kind,
            Name,
            () =>
            {
                var stream = OpenStream();
                return stream is null ? null : new QuestionPreviewMediaStream(stream.Stream, stream.Length);
            }));
    }

    protected override IMedia GetMedia() => _mediaGetter();

    protected override void OnError(Exception exc) => MainViewModel.ShowError(exc);
}
