using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SIQuester.Avalonia.Helpers;
using SIQuester.Avalonia.Localization;
using SIQuester.ViewModel;
using SIQuester.ViewModel.Contracts;

namespace SIQuester.Avalonia.Views;

public partial class MediaItemPreview : UserControl
{
    public static readonly StyledProperty<MediaItemViewModel?> ItemProperty =
        AvaloniaProperty.Register<MediaItemPreview, MediaItemViewModel?>(nameof(Item));

    private CancellationTokenSource? _loadCancellation;
    private Bitmap? _bitmap;
    private IMediaPreviewSession? _playbackSession;
    private NativeWebView? _webView;
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
        ResetPreview(releasePlaybackHost: false);
        var item = Item;

        if (item == null)
        {
            StatusText.Text = UiStrings.NoMediaSelected;
            return;
        }

        if (!item.IsImage)
        {
            if (item.IsPlayable)
            {
                StartPlayback(item);
            }
            else
            {
                StatusText.Text = UiStrings.PreviewUnavailable;
            }

            return;
        }

        StatusText.Text = UiStrings.LoadingImage;
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        _ = LoadAsync(item, cancellation);
    }

    private void StartPlayback(MediaItemViewModel item)
    {
        try
        {
            var session = item.CreatePreviewSession();
            _playbackSession = session;
            if (!session.IsAvailable || session.Source is null)
            {
                StatusText.Text = GetUnavailableMessage(session);
                return;
            }

            StatusText.Text = UiStrings.LoadingMediaPreview;
            var webView = _webView;
            if (webView is null)
            {
                webView = new NativeWebView { Source = session.Source };
                webView.EnvironmentRequested += WebView_EnvironmentRequested;
                webView.NavigationStarted += WebView_NavigationStarted;
                webView.NavigationCompleted += WebView_NavigationCompleted;
                webView.NewWindowRequested += WebView_NewWindowRequested;
                webView.AdapterDestroyed += WebView_AdapterDestroyed;
                _webView = webView;
            }
            else
            {
                webView.Navigate(session.Source);
            }

            PlaybackHost.Content = webView;
        }
        catch (Exception)
        {
            StopPlayback(releaseHost: false);
            StatusText.Text = UiStrings.MediaPreviewFailed;
        }
    }

    private static string GetUnavailableMessage(IMediaPreviewSession session) => session.BackendRequirement switch
    {
        QuestionPreviewBackendRequirement.LinuxWebKit => UiStrings.QuestionPreviewLinuxBackendUnavailable,
        QuestionPreviewBackendRequirement.WindowsWebView2 => UiStrings.QuestionPreviewWindowsBackendUnavailable,
        QuestionPreviewBackendRequirement.UnsupportedPlatform => UiStrings.QuestionPreviewPlatformUnsupported,
        _ => UiStrings.PreviewUnavailable,
    };

    private static void WebView_EnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e) =>
        e.EnableDevTools = false;

    private void WebView_NavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (_playbackSession?.Source is not { } source || !IsAllowedNavigation(source, e.Request))
        {
            e.Cancel = true;
        }
    }

    private void WebView_NavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (!ReferenceEquals(sender, _webView)
            || _playbackSession?.Source is not { } source
            || !IsAllowedNavigation(source, e.Request))
        {
            return;
        }

        StatusText.Text = e.IsSuccess ? null : UiStrings.MediaPreviewFailed;
    }

    private static void WebView_NewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e) =>
        e.Handled = true;

    private void WebView_AdapterDestroyed(object? sender, WebViewAdapterEventArgs e)
    {
        if (ReferenceEquals(sender, _webView))
        {
            StatusText.Text = UiStrings.MediaPreviewFailed;
        }
    }

    private static bool IsAllowedNavigation(Uri source, Uri? requested) =>
        requested is { IsAbsoluteUri: true }
        && string.Equals(source.Scheme, requested.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(source.Host, requested.Host, StringComparison.OrdinalIgnoreCase)
        && source.Port == requested.Port
        && string.Equals(source.AbsolutePath, requested.AbsolutePath, StringComparison.Ordinal)
        && string.IsNullOrEmpty(requested.Query)
        && string.IsNullOrEmpty(requested.UserInfo)
        && (string.Equals(source.Fragment, requested.Fragment, StringComparison.Ordinal)
            || string.IsNullOrEmpty(requested.Fragment));

    private async Task LoadAsync(MediaItemViewModel item, CancellationTokenSource cancellation)
    {
        try
        {
            var streamInfo = await item.OpenStreamAsync(cancellation.Token);
            var bitmap = await BoundedBitmapLoader.LoadAsync(streamInfo, cancellation.Token);

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

    private void StopLoad() => ResetPreview(releasePlaybackHost: true);

    private void ResetPreview(bool releasePlaybackHost)
    {
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        PreviewImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        StopPlayback(releasePlaybackHost);
        StatusText.Text = null;
    }

    private void StopPlayback(bool releaseHost)
    {
        var webView = _webView;
        if (webView is not null)
        {
            webView.Stop();

            if (releaseHost)
            {
                webView.EnvironmentRequested -= WebView_EnvironmentRequested;
                webView.NavigationStarted -= WebView_NavigationStarted;
                webView.NavigationCompleted -= WebView_NavigationCompleted;
                webView.NewWindowRequested -= WebView_NewWindowRequested;
                webView.AdapterDestroyed -= WebView_AdapterDestroyed;
                _webView = null;
            }
        }

        PlaybackHost.Content = null;
        _playbackSession?.Dispose();
        _playbackSession = null;
    }
}
