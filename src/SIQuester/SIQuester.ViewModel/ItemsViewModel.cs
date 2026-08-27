using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <inheritdoc cref="IItemsViewModel" />
public abstract class ItemsViewModel<T> : ObservableCollection<T>, IItemsViewModel
{
    public ICommand AddItem { get; private set; }

    public new SimpleCommand RemoveItem { get; private set; }

    public SimpleCommand MoveLeft { get; private set; }

    public SimpleCommand MoveRight { get; private set; }

    public abstract QDocument? OwnerDocument { get; }

    private int _currentPosition;

    public int CurrentPosition
    {
        get => _currentPosition;
        set
        {
            if (_currentPosition != value)
            {
                var oldValue = _currentItem;
                _currentPosition = value;

                if (_currentPosition > -1 && _currentPosition < Count)
                {
                    _currentItem = this[_currentPosition];
                }
                else
                {
                    _currentItem = default;
                }

                OnCurrentItemChanged(oldValue, _currentItem);
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentPosition)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentItem)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentItemValue)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasCurrentItem)));
                UpdateCommands();
            }
        }
    }

    private T? _currentItem;

    public T? CurrentItem
    {
        get => _currentItem;
        set
        {
            if (!Equals(_currentItem, value))
            {
                var oldValue = _currentItem;
                _currentItem = value;
                var newPosition = _currentItem == null ? -1 : IndexOf(_currentItem);

                if (_currentPosition != newPosition)
                {
                    _currentPosition = newPosition;
                    OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentPosition)));
                    OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasCurrentItem)));
                    UpdateCommands();
                }

                OnCurrentItemChanged(oldValue, value);

                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentItem)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentItemValue)));
            }
        }
    }

    /// <summary>
    /// Gets or replaces the item at <see cref="CurrentPosition" />.
    /// Unlike <see cref="CurrentItem" />, this property preserves the selected index when a collection contains equal items.
    /// </summary>
    public T? CurrentItemValue
    {
        get => HasCurrentItem ? this[_currentPosition] : default;
        set
        {
            if (!HasCurrentItem || EqualityComparer<T>.Default.Equals(this[_currentPosition], value))
            {
                return;
            }

            var oldValue = this[_currentPosition];
            this[_currentPosition] = value!;
            _currentItem = value;
            OnCurrentItemChanged(oldValue, value);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentItem)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentItemValue)));
        }
    }

    public bool HasCurrentItem => _currentPosition > -1 && _currentPosition < Count;

    protected virtual void OnCurrentItemChanged(T? oldValue, T? newValue) { }

    public void SetCurrentItem(object item)
    {
        CurrentItem = (T)item;
    }

    protected ItemsViewModel() => Init();

    protected ItemsViewModel(IEnumerable<T> collection) : base(collection) => Init();

    private void Init()
    {
        AddItem = new SimpleCommand(AddItem_Executed);
        RemoveItem = new SimpleCommand(RemoveItem_Executed);

        MoveLeft = new SimpleCommand(MoveLeft_Executed);
        MoveRight = new SimpleCommand(MoveRight_Executed);

        PropertyChanged += ItemsViewModel_PropertyChanged;
    }

    private void ItemsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Count))
        {
            var newPosition = Count == 0 ? -1 : Math.Clamp(_currentPosition, 0, Count - 1);

            if (_currentPosition != newPosition)
            {
                CurrentPosition = newPosition;
            }
            else
            {
                _currentItem = HasCurrentItem ? this[_currentPosition] : default;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentItem)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentItemValue)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasCurrentItem)));
            }

            UpdateCommands();
        }
    }

    public void UpdateCommands()
    {
        var position = _currentPosition;

        MoveLeft.CanBeExecuted = position > 0;
        MoveRight.CanBeExecuted = position > -1 && position + 1 < Count;

        RemoveItem.CanBeExecuted = HasCurrentItem && CanRemove();
    }

    protected virtual bool CanRemove() => true;

    private void AddItem_Executed(object? arg)
    {
        Add((T)(arg ?? ""));
        CurrentPosition = Count - 1;
    }

    private void RemoveItem_Executed(object? arg)
    {
        try
        {
            using var change = OwnerDocument?.OperationsManager.BeginComplexChange();

            if (arg != null)
            {
                Remove((T)arg);
                change?.Commit();
            }
            else if (_currentPosition > -1 && _currentPosition < Count)
            {
                OnRemoveAt(_currentPosition);
                change?.Commit();
            }

            UpdateCommands();
        }
        catch (Exception ex)
        {
            PlatformSpecific.PlatformManager.Instance.ShowErrorMessage(ex.Message);
        }
    }

    protected virtual void OnRemoveAt(int index) => RemoveAt(index);

    private void MoveLeft_Executed(object? arg)
    {
        var index = _currentPosition;

        if (index < 1 || index >= Count)
        {
            return;
        }

        var document = OwnerDocument;

        if (document == null)
        {
            return;
        }

        try
        {
            using var change = document.OperationsManager.BeginComplexChange();

            var currentItem = this[index];
            (this[index - 1], this[index]) = (this[index], this[index - 1]);
            CurrentPosition--;
            CurrentItem = currentItem;

            change.Commit();
        }
        catch (Exception exc)
        {
            PlatformSpecific.PlatformManager.Instance.ShowExclamationMessage(exc.ToString());
        }
    }

    private void MoveRight_Executed(object? arg)
    {
        var index = _currentPosition;

        var document = OwnerDocument;

        if (document == null)
        {
            return;
        }

        try
        {
            using var change = document.OperationsManager.BeginComplexChange();

            var currentItem = this[index];
            (this[index + 1], this[index]) = (this[index], this[index + 1]);
            CurrentPosition++;
            CurrentItem = currentItem;

            change.Commit();
        }
        catch (Exception exc)
        {
            PlatformSpecific.PlatformManager.Instance.ShowExclamationMessage(exc.ToString());
        }
    }
}
