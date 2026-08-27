using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using SIQuester.ViewModel.Workspaces.Dialogs;
using Utils.Web;

namespace SIQuester.Avalonia.Views;

public partial class QuestionPreviewView : UserControl
{
    private static readonly TimeSpan BridgeReadinessTimeout = TimeSpan.FromSeconds(10);
    private readonly Lock _sendSync = new();
    private QuestionPlayViewModel? _viewModel;
    private NativeWebView? _webView;
    private CancellationTokenSource? _sendLifetime;
    private Task _sendTail = Task.CompletedTask;
    private Task _bridgeReadinessTask = Task.CompletedTask;
    private bool _isBridgeReady;
    private bool _isAttached;

    public QuestionPreviewView()
    {
        InitializeComponent();
        DataContextChanged += QuestionPreviewView_DataContextChanged;
        AttachedToVisualTree += QuestionPreviewView_AttachedToVisualTree;
        DetachedFromVisualTree += QuestionPreviewView_DetachedFromVisualTree;
    }

    private void QuestionPreviewView_DataContextChanged(object? sender, EventArgs e)
    {
        DetachHost();
        _viewModel = DataContext as QuestionPlayViewModel;

        if (_isAttached)
        {
            AttachHost();
        }
    }

    private void QuestionPreviewView_AttachedToVisualTree(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        _viewModel = DataContext as QuestionPlayViewModel;
        AttachHost();
    }

    private void QuestionPreviewView_DetachedFromVisualTree(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        DetachHost();
    }

    private void AttachHost()
    {
        if (_webView is not null
            || _viewModel is not { IsPreviewAvailable: true, Source: { } source } viewModel)
        {
            return;
        }

        viewModel.SetPreviewReady(false);
        _isBridgeReady = false;
        _sendLifetime = new CancellationTokenSource();
        var webView = new NativeWebView { Source = source };
        webView.EnvironmentRequested += WebView_EnvironmentRequested;
        webView.NavigationStarted += WebView_NavigationStarted;
        webView.NavigationCompleted += WebView_NavigationCompleted;
        webView.NewWindowRequested += WebView_NewWindowRequested;
        webView.WebMessageReceived += WebView_WebMessageReceived;
        webView.AdapterDestroyed += WebView_AdapterDestroyed;
        viewModel.SendJsonMessage += ViewModel_SendJsonMessage;
        _webView = webView;
        PreviewContentHost.Content = webView;
        _bridgeReadinessTask = WaitForBridgeReadinessAsync(webView, viewModel, _sendLifetime.Token);
    }

    private static void WebView_EnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e) =>
        e.EnableDevTools = false;

    private void WebView_NavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (_viewModel?.Source is not { } source
            || !QuestionPreviewWebBridge.IsAllowedNavigation(source, e.Request))
        {
            e.Cancel = true;
        }
    }

    private void WebView_NavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        var webView = _webView;
        var viewModel = _viewModel;

        if (webView is null
            || !ReferenceEquals(sender, webView)
            || viewModel?.Source is not { } source
            || !QuestionPreviewWebBridge.IsAllowedNavigation(source, e.Request))
        {
            return;
        }

        if (!e.IsSuccess)
        {
            viewModel.ReportPreviewHostFailure(
                new InvalidOperationException("Question preview navigation did not complete."));
        }
    }

    private static void WebView_NewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e) =>
        e.Handled = true;

    private void WebView_WebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (ReferenceEquals(sender, _webView)
            && _viewModel is { } viewModel
            && QuestionPreviewWebBridge.TryValidateInboundMessage(e.Body, out var message))
        {
            if (QuestionPreviewWebBridge.IsReadyMessage(message))
            {
                _isBridgeReady = true;
                viewModel.SetPreviewReady(true);
                return;
            }

            ((IWebInterop)viewModel).OnMessage(message);
        }
    }

    private async Task WaitForBridgeReadinessAsync(
        NativeWebView webView,
        QuestionPlayViewModel viewModel,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(BridgeReadinessTimeout, cancellationToken);

            if (!_isBridgeReady && ReferenceEquals(webView, _webView))
            {
                viewModel.ReportPreviewHostFailure(
                    new TimeoutException("Question preview bridge did not initialize in time."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void WebView_AdapterDestroyed(object? sender, WebViewAdapterEventArgs e)
    {
        if (ReferenceEquals(sender, _webView))
        {
            _viewModel?.SetPreviewReady(false);
        }
    }

    private void ViewModel_SendJsonMessage(string message)
    {
        lock (_sendSync)
        {
            var previous = _sendTail;
            _sendTail = SendMessageAfterAsync(previous, message, _sendLifetime?.Token ?? default);
        }
    }

    private async Task SendMessageAfterAsync(Task previous, string message, CancellationToken cancellationToken)
    {
        try
        {
            await previous.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var script = QuestionPreviewWebBridge.BuildHostDispatchScript(message);
            Task<string?>? invocation = null;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                invocation = _webView?.InvokeScript(script)
                    ?? Task.FromException<string?>(new InvalidOperationException("Question preview host is detached."));
            });

            await (invocation ?? Task.FromException<string?>(
                new InvalidOperationException("Question preview script invocation was not scheduled.")))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _viewModel?.ReportPreviewHostFailure(exception);
                }
            });
        }
    }

    private void DetachHost()
    {
        var webView = _webView;
        var viewModel = _viewModel;
        _webView = null;

        if (viewModel is not null)
        {
            viewModel.SendJsonMessage -= ViewModel_SendJsonMessage;
            viewModel.SetPreviewReady(false);
        }

        _sendLifetime?.Cancel();
        _sendLifetime?.Dispose();
        _sendLifetime = null;
        _isBridgeReady = false;
        _bridgeReadinessTask = Task.CompletedTask;

        lock (_sendSync)
        {
            _sendTail = Task.CompletedTask;
        }

        if (webView is not null)
        {
            webView.EnvironmentRequested -= WebView_EnvironmentRequested;
            webView.NavigationStarted -= WebView_NavigationStarted;
            webView.NavigationCompleted -= WebView_NavigationCompleted;
            webView.NewWindowRequested -= WebView_NewWindowRequested;
            webView.WebMessageReceived -= WebView_WebMessageReceived;
            webView.AdapterDestroyed -= WebView_AdapterDestroyed;
            webView.Stop();
        }

        PreviewContentHost.Content = null;
    }
}
