using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SIPackages.Core;
using SIQuester.Avalonia.Helpers;
using SIQuester.ViewModel;
using System.ComponentModel;

namespace SIQuester.Avalonia.Views;

public partial class PointSelectionWindow : Window
{
    private readonly StreamInfo? _streamInfo;
    private readonly CancellationTokenSource _loadCancellation = new();
    private Bitmap? _bitmap;

    public PointSelectionController Controller { get; }

    public PointSelectionWindow() : this("", 0.0, null) { }

    public PointSelectionWindow(string answer, double deviation, StreamInfo? streamInfo)
    {
        Controller = new PointSelectionController(answer, deviation);
        _streamInfo = streamInfo;
        InitializeComponent();
        DataContext = Controller;
        Opened += PointSelectionWindow_Opened;
        Closed += PointSelectionWindow_Closed;
        ReferenceGrid.PropertyChanged += ReferenceGrid_PropertyChanged;
        Controller.PropertyChanged += Controller_PropertyChanged;
    }

    private async void PointSelectionWindow_Opened(object? sender, EventArgs e)
    {
        try
        {
            _bitmap = await BoundedBitmapLoader.LoadAsync(_streamInfo, _loadCancellation.Token);
            TargetImage.Source = _bitmap;
            LoadingText.IsVisible = false;
            PointerSurface.IsHitTestVisible = true;
            PointerSurface.Focus();
            UpdateVisuals();
        }
        catch (OperationCanceledException) when (_loadCancellation.IsCancellationRequested)
        {
        }
        catch (Exception) when (_loadCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            LoadingText.IsVisible = false;
            ImageErrorText.IsVisible = true;
            PointerSurface.IsHitTestVisible = false;
        }
    }

    private void PointSelectionWindow_Closed(object? sender, EventArgs e)
    {
        _loadCancellation.Cancel();
        Controller.PropertyChanged -= Controller_PropertyChanged;
        ReferenceGrid.PropertyChanged -= ReferenceGrid_PropertyChanged;
        _streamInfo?.Stream.Dispose();
        _bitmap?.Dispose();
        _loadCancellation.Dispose();
    }

    private void Controller_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PointSelectionController.CurrentAnswer)
            || e.PropertyName == nameof(PointSelectionController.CurrentDeviation))
        {
            UpdateVisuals();
        }
    }

    private void ReferenceGrid_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
        {
            UpdateVisuals();
        }
    }

    private void PointerSurface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_bitmap == null || !e.GetCurrentPoint(ReferenceGrid).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(ReferenceGrid);
        PointerSurface.Focus();
        Controller.SelectFromViewport(
            position.X,
            position.Y,
            ReferenceGrid.Bounds.Width,
            ReferenceGrid.Bounds.Height,
            _bitmap.PixelSize.Width,
            _bitmap.PixelSize.Height);
    }

    private void PointerSurface_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_bitmap == null)
        {
            return;
        }

        const double increment = 0.01;
        var handled = e.Key switch
        {
            Key.Left => Controller.NudgeSelection(-increment, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height),
            Key.Right => Controller.NudgeSelection(increment, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height),
            Key.Up => Controller.NudgeSelection(0, -increment, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height),
            Key.Down => Controller.NudgeSelection(0, increment, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height),
            _ => false,
        };

        e.Handled = handled;
    }

    private void UpdateVisuals()
    {
        if (_bitmap == null
            || !Controller.TryGetVisualLayout(
                ReferenceGrid.Bounds.Width,
                ReferenceGrid.Bounds.Height,
                _bitmap.PixelSize.Width,
                _bitmap.PixelSize.Height,
                out var layout))
        {
            SelectionMarker.IsVisible = false;
            ErrorCircle.IsVisible = false;
            return;
        }

        SelectionMarker.IsVisible = true;
        ErrorCircle.IsVisible = true;
        Canvas.SetLeft(SelectionMarker, layout.MarkerX - SelectionMarker.Width / 2);
        Canvas.SetTop(SelectionMarker, layout.MarkerY - SelectionMarker.Height / 2);
        ErrorCircle.Width = layout.Radius * 2;
        ErrorCircle.Height = layout.Radius * 2;
        Canvas.SetLeft(ErrorCircle, layout.MarkerX - layout.Radius);
        Canvas.SetTop(ErrorCircle, layout.MarkerY - layout.Radius);
    }

    private void Ok_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => Close(false);

}
