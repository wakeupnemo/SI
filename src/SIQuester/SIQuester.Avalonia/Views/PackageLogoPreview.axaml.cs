using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SIQuester.Avalonia.Helpers;
using SIQuester.ViewModel;
using System.ComponentModel;

namespace SIQuester.Avalonia.Views;

public partial class PackageLogoPreview : UserControl
{
    public static readonly StyledProperty<PackageViewModel?> PackageProperty =
        AvaloniaProperty.Register<PackageLogoPreview, PackageViewModel?>(nameof(Package));

    private PackageViewModel? _subscribedPackage;
    private CancellationTokenSource? _loadCancellation;
    private Task? _loadTask;
    private Bitmap? _bitmap;
    private bool _isAttached;

    public PackageViewModel? Package
    {
        get => GetValue(PackageProperty);
        set => SetValue(PackageProperty, value);
    }

    public PackageLogoPreview()
    {
        InitializeComponent();
        AttachedToVisualTree += PackageLogoPreview_AttachedToVisualTree;
        DetachedFromVisualTree += PackageLogoPreview_DetachedFromVisualTree;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PackageProperty && _isAttached)
        {
            AttachPackage(Package);
        }
    }

    private void PackageLogoPreview_AttachedToVisualTree(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        AttachPackage(Package);
    }

    private void PackageLogoPreview_DetachedFromVisualTree(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        AttachPackage(null);
    }

    private void AttachPackage(PackageViewModel? package)
    {
        if (_subscribedPackage != null)
        {
            _subscribedPackage.PropertyChanged -= Package_PropertyChanged;
        }

        _subscribedPackage = package;

        if (_subscribedPackage != null)
        {
            _subscribedPackage.PropertyChanged += Package_PropertyChanged;
        }

        StartLoad();
    }

    private void Package_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackageViewModel.Logo))
        {
            StartLoad();
        }
    }

    private void StartLoad()
    {
        StopLoad();
        LogoImage.Source = null;
        LoadingText.IsVisible = false;
        ImageErrorText.IsVisible = false;

        if (!_isAttached || _subscribedPackage?.HasLogo != true)
        {
            return;
        }

        LoadingText.IsVisible = true;
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        _loadTask = LoadAsync(_subscribedPackage, cancellation);
    }

    private async Task LoadAsync(PackageViewModel package, CancellationTokenSource cancellation)
    {
        try
        {
            var streamInfo = await package.OpenLogoStreamAsync(cancellation.Token);
            var bitmap = await BoundedBitmapLoader.LoadAsync(
                streamInfo,
                cancellation.Token);

            if (!ReferenceEquals(_loadCancellation, cancellation) || cancellation.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }

            _bitmap = bitmap;
            LogoImage.Source = bitmap;
            LoadingText.IsVisible = false;
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
                LoadingText.IsVisible = false;
                ImageErrorText.IsVisible = true;
            }
        }
    }

    private void StopLoad()
    {
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        _loadTask = null;

        LogoImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
