using Avalonia.Controls;
using SIPackages;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class PointAnswerEditorView : UserControl
{
    private PointAnswerViewModel? _subscribedViewModel;
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

        var streamInfo = viewModel.GetImageStream(contentItem.Value);
        var dialog = new PointSelectionWindow(viewModel.Answer, viewModel.Deviation, streamInfo);

        if (await dialog.ShowDialog<bool>(owner))
        {
            dialog.Controller.ApplyTo(viewModel);
        }
    }
}
