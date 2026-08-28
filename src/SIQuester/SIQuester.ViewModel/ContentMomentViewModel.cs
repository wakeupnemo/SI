using SIPackages.Core;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Represents a transient presentation moment formed from consecutive canonical content items.
/// This view model does not own or serialize a second content model.
/// </summary>
public sealed class ContentMomentViewModel
{
    private readonly ContentItemsViewModel _owner;

    /// <summary>
    /// One-based moment number in the current content sequence.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// Canonical content items played in this moment.
    /// </summary>
    public IReadOnlyList<ContentItemViewModel> Items { get; }

    public IReadOnlyList<ContentItemViewModel> ScreenItems { get; }

    public IReadOnlyList<ContentItemViewModel> ReplicItems { get; }

    public IReadOnlyList<ContentItemViewModel> BackgroundItems { get; }

    public IReadOnlyList<ContentItemViewModel> OtherItems { get; }

    /// <summary>
    /// Gets whether this moment can be joined with the following moment.
    /// </summary>
    public bool CanMergeWithNext { get; }

    public bool HasScreenItems => ScreenItems.Count > 0;

    public bool HasReplicItems => ReplicItems.Count > 0;

    public bool HasBackgroundItems => BackgroundItems.Count > 0;

    public bool HasOtherItems => OtherItems.Count > 0;

    /// <summary>
    /// Joins this moment with the following moment by changing the canonical group boundary.
    /// </summary>
    public SimpleCommand MergeWithNext { get; }

    /// <summary>
    /// Moves the complete moment one position earlier without changing its internal grouping.
    /// </summary>
    public SimpleCommand MoveEarlier { get; }

    /// <summary>
    /// Moves the complete moment one position later without changing its internal grouping.
    /// </summary>
    public SimpleCommand MoveLater { get; }

    internal ContentMomentViewModel(
        ContentItemsViewModel owner,
        int number,
        IReadOnlyList<ContentItemViewModel> items,
        bool canMergeWithNext)
    {
        _owner = owner;
        Number = number;
        Items = items;
        ScreenItems = items.Where(item => item.Model.Placement == ContentPlacements.Screen).ToArray();
        ReplicItems = items.Where(item => item.Model.Placement == ContentPlacements.Replic).ToArray();
        BackgroundItems = items.Where(item => item.Model.Placement == ContentPlacements.Background).ToArray();
        OtherItems = items.Where(item => item.Model.Placement != ContentPlacements.Screen
            && item.Model.Placement != ContentPlacements.Replic
            && item.Model.Placement != ContentPlacements.Background).ToArray();
        CanMergeWithNext = canMergeWithNext;
        MergeWithNext = new SimpleCommand(_ => _owner.MergeWithNext(this))
        {
            CanBeExecuted = canMergeWithNext,
        };
        MoveEarlier = new SimpleCommand(_ => _owner.MoveMoment(this, -1)) { CanBeExecuted = number > 1 };
        MoveLater = new SimpleCommand(_ => _owner.MoveMoment(this, 1)) { CanBeExecuted = canMergeWithNext };
    }
}
