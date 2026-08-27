namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Dispatches view-model state updates to the frontend UI thread.
/// </summary>
public interface IUiDispatcher
{
    ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default);
}
