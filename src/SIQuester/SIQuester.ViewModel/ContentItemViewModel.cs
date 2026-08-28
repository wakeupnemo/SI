using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using System.ComponentModel;
using Utils.Commands;

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
    private ContentItemsViewModel? _owner;

    public ContentItemsViewModel? Owner
    {
        get => _owner;
        set
        {
            if (ReferenceEquals(_owner, value))
            {
                return;
            }

            _owner = value;
            Select.CanBeExecuted = value != null;
            SeparateFromNext.CanBeExecuted = value != null && IsJoinedWithNext;
        }
    }

    public override string Type => CollectionNames.TryGetCollectionName(Model.Type) ?? Model.Type;

    /// <summary>
    /// Gets a stable accessible description for the content card.
    /// </summary>
    public string AutomationName => string.IsNullOrWhiteSpace(Model.Value) ? Type : $"{Type}: {Model.Value}";

    public decimal DurationSeconds
    {
        get => (decimal)Model.Duration.TotalSeconds;
        set => Model.Duration = TimeSpan.FromSeconds((double)Math.Max(value, 0));
    }

    /// <summary>
    /// Gets or sets whether this item is played in the same moment as the following item.
    /// </summary>
    public bool IsJoinedWithNext
    {
        get => !Model.WaitForFinish;
        set => Model.WaitForFinish = !value;
    }

    /// <summary>
    /// Gets whether this item is selected for detailed editing.
    /// </summary>
    public bool IsCurrent { get; internal set; }

    internal void SetCurrent(bool value)
    {
        if (IsCurrent == value)
        {
            return;
        }

        IsCurrent = value;
        OnPropertyChanged(nameof(IsCurrent));
    }

    public bool HasDuration => Model.Duration > TimeSpan.Zero;

    /// <summary>
    /// Selects this item in its owning content editor.
    /// </summary>
    public SimpleCommand Select { get; }

    /// <summary>
    /// Ends the current presentation moment after this item.
    /// </summary>
    public SimpleCommand SeparateFromNext { get; }

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
        Select = new SimpleCommand(_ => Owner?.SelectItem(this)) { CanBeExecuted = false };
        SeparateFromNext = new SimpleCommand(_ => Owner?.SeparateAfter(this)) { CanBeExecuted = false };
        Model.PropertyChanged += Model_PropertyChanged;
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContentItem.Duration))
        {
            OnPropertyChanged(nameof(DurationSeconds));
            OnPropertyChanged(nameof(HasDuration));
        }
        else if (e.PropertyName == nameof(ContentItem.Type))
        {
            OnPropertyChanged(nameof(Type));
            OnPropertyChanged(nameof(AutomationName));
            Owner?.RefreshMoments();
        }
        else if (e.PropertyName == nameof(ContentItem.Value))
        {
            OnPropertyChanged(nameof(AutomationName));
        }
        else if (e.PropertyName == nameof(ContentItem.Placement))
        {
            Owner?.RefreshMoments();
        }
        else if (e.PropertyName == nameof(ContentItem.WaitForFinish))
        {
            OnPropertyChanged(nameof(IsJoinedWithNext));
            SeparateFromNext.CanBeExecuted = Owner != null && IsJoinedWithNext;
            Owner?.RefreshMoments();
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
