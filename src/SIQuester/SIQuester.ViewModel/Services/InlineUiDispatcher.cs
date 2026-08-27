using SIQuester.ViewModel.Contracts;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Provides a framework-neutral dispatcher fallback for non-UI hosts and tests.
/// </summary>
internal sealed class InlineUiDispatcher : IUiDispatcher
{
    public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return ValueTask.CompletedTask;
    }
}
