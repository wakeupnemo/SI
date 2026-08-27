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

    private readonly Func<IMedia> _mediaGetter;

    public MediaItemViewModel(
        Named named,
        string type,
        Func<IMedia> mediaGetter,
        Func<StreamInfo?> streamGetter)
    {
        Model = named;
        Type = type;
        _mediaGetter = mediaGetter;
        _streamGetter = streamGetter;
    }

    /// <summary>
    /// Opens the original package bytes without invoking a platform media backend.
    /// </summary>
    public StreamInfo? OpenStream() => _streamGetter();

    protected override IMedia GetMedia() => _mediaGetter();

    protected override void OnError(Exception exc) => MainViewModel.ShowError(exc);
}
