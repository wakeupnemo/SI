using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SIQuester.Avalonia.Helpers;
using SIQuester.Avalonia.Localization;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class MediaItemPreview : UserControl
{
    public static readonly StyledProperty<MediaItemViewModel?> ItemProperty =
        AvaloniaProperty.Register<MediaItemPreview, MediaItemViewModel?>(nameof(Item));

    private CancellationTokenSource? _loadCancellation;
    private Bitmap? _bitmap;
    private bool _isAttached;

    public MediaItemViewModel? Item
    {
        get => GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public MediaItemPreview()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            StartLoad();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            StopLoad();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemProperty && _isAttached)
        {
            StartLoad();
        }
    }

    private void StartLoad()
    {
        StopLoad();
        var item = Item;

        if (item == null)
        {
            StatusText.Text = UiStrings.NoMediaSelected;
            return;
        }

        if (!item.IsImage)
        {
            StatusText.Text = UiStrings.PreviewUnavailable;
            return;
        }

        StatusText.Text = UiStrings.LoadingImage;
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        _ = LoadAsync(item, cancellation);
    }

    private async Task LoadAsync(MediaItemViewModel item, CancellationTokenSource cancellation)
    {
        try
        {
            var bitmap = await BoundedBitmapLoader.LoadAsync(item.OpenStream(), cancellation.Token);

            if (!ReferenceEquals(_loadCancellation, cancellation) || cancellation.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }

            _bitmap = bitmap;
            PreviewImage.Source = bitmap;
            StatusText.Text = null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                StatusText.Text = UiStrings.ImageUnavailable;
            }
        }
    }

    private void StopLoad()
    {
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        PreviewImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        StatusText.Text = null;
    }
}
