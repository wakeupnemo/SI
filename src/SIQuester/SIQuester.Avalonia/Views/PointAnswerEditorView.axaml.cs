using Avalonia.Controls;
using SIPackages;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class PointAnswerEditorView : UserControl
{
    private PointAnswerViewModel? _subscribedViewModel;
    private CancellationTokenSource? _requestCancellation;
    private bool _isAttached;

    public PointAnswerEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateSubscription();
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            UpdateSubscription();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            Unsubscribe();
        };
    }

    private void UpdateSubscription()
    {
        var viewModel = _isAttached ? DataContext as PointAnswerViewModel : null;

        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        Unsubscribe();
        _subscribedViewModel = viewModel;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.SelectPointRequest += OnSelectPointRequest;
        }
    }

    private void Unsubscribe()
    {
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.SelectPointRequest -= OnSelectPointRequest;
            _subscribedViewModel = null;
        }
    }

    private async void OnSelectPointRequest(ContentItem contentItem)
    {
        var viewModel = _subscribedViewModel;
        var owner = TopLevel.GetTopLevel(this) as Window;

        if (viewModel == null || owner == null)
        {
            return;
        }

        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _requestCancellation = cancellation;
        SIPackages.Core.StreamInfo? streamInfo = null;

        try
        {
            streamInfo = await viewModel.GetImageStreamAsync(contentItem.Value, cancellation.Token);

            if (cancellation.IsCancellationRequested
                || !ReferenceEquals(_requestCancellation, cancellation)
                || !ReferenceEquals(_subscribedViewModel, viewModel))
            {
                return;
            }

            var dialog = new PointSelectionWindow(viewModel.Answer, viewModel.Deviation, streamInfo);
            streamInfo = null; // The dialog now owns the stream.

            if (await dialog.ShowDialog<bool>(owner))
            {
                dialog.Controller.ApplyTo(viewModel);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            streamInfo?.Stream.Dispose();

            if (ReferenceEquals(_requestCancellation, cancellation))
            {
                _requestCancellation = null;
            }

            cancellation.Dispose();
        }
    }
}
