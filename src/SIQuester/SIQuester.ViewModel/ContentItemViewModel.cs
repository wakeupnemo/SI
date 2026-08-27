using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using System.ComponentModel;

namespace SIQuester.ViewModel;

/// <summary>
/// Represents content item view model.
/// </summary>
/// <inheritdoc cref="MediaOwnerViewModel" />
public sealed class ContentItemViewModel : MediaOwnerViewModel
{
    public static IReadOnlyList<string> AvailablePlacements { get; } =
        new[] { ContentPlacements.Screen, ContentPlacements.Replic, ContentPlacements.Background };

    /// <summary>
    /// Original model wrapped by this view model.
    /// </summary>
    public ContentItem Model { get; }

    /// <summary>
    /// View model that contains current view model.
    /// </summary>
    public ContentItemsViewModel? Owner { get; set; }

    public override string Type => CollectionNames.TryGetCollectionName(Model.Type) ?? Model.Type;

    public decimal DurationSeconds
    {
        get => (decimal)Model.Duration.TotalSeconds;
        set => Model.Duration = TimeSpan.FromSeconds((double)Math.Max(value, 0));
    }

    private bool _isExpanded = true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public ContentItemViewModel(ContentItem model)
    {
        Model = model;
        Model.PropertyChanged += Model_PropertyChanged;
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContentItem.Duration))
        {
            OnPropertyChanged(nameof(DurationSeconds));
        }
        else if (e.PropertyName == nameof(ContentItem.Type))
        {
            OnPropertyChanged(nameof(Type));
        }
    }

    protected override IMedia GetMedia()
    {
        if (Owner == null)
        {
            throw new InvalidOperationException("OwnerScenario is undefined");
        }

        if (Owner.OwnerDocument == null)
        {
            throw new InvalidOperationException("OwnerDocument is undefined");
        }

        return Owner.OwnerDocument.Wrap(Model);
    }

    protected override void OnError(Exception exc) => Owner?.OwnerDocument?.OnError(exc);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Model.PropertyChanged -= Model_PropertyChanged;
        }

        base.Dispose(disposing);
    }
}
