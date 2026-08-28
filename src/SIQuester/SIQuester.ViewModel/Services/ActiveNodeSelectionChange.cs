namespace SIQuester.ViewModel.Services;

/// <summary>
/// Keeps document selection synchronized with an undoable structural operation.
/// </summary>
internal sealed class ActiveNodeSelectionChange : IChange
{
    private readonly QDocument _document;
    private readonly IItemViewModel? _previousItem;
    private readonly IItemViewModel _currentItem;

    internal ActiveNodeSelectionChange(
        QDocument document,
        IItemViewModel? previousItem,
        IItemViewModel currentItem) =>
        (_document, _previousItem, _currentItem) = (document, previousItem, currentItem);

    public void Undo() => Select(_previousItem);

    public void Redo() => Select(_currentItem);

    internal void Apply() => Select(_currentItem);

    private void Select(IItemViewModel? item)
    {
        if (_document.ActiveNode != null)
        {
            _document.ActiveNode.IsSelected = false;
        }

        if (item != null)
        {
            item.IsSelected = true;
        }

        _document.ActiveNode = item;
    }
}
