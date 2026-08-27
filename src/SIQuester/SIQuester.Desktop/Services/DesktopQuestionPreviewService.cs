using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using SIQuester.ViewModel.Contracts;

namespace SIQuester.Desktop.Services;

/// <summary>
/// Probes the official native WebView runtimes lazily and owns the controlled application-content origin.
/// </summary>
internal sealed class DesktopQuestionPreviewService : IQuestionPreviewService, IMediaPreviewService, IDisposable
{
    private readonly Lock _sync = new();
    private readonly ILogger<DesktopQuestionPreviewService> _logger;
    private QuestionPreviewHostDescriptor? _descriptor;
    private QuestionPreviewContentServer? _contentServer;
    private bool _disposed;

    public DesktopQuestionPreviewService(ILogger<DesktopQuestionPreviewService> logger) => _logger = logger;

    public QuestionPreviewHostDescriptor GetHostDescriptor()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_descriptor is not null)
            {
                return _descriptor;
            }

            var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot");

            if (!QuestionPreviewContentServer.HasRequiredAssets(assetsDirectory))
            {
                _logger.LogWarning("Question preview assets are unavailable");
                return _descriptor = QuestionPreviewHostDescriptor.Unavailable(
                    QuestionPreviewAvailability.AssetsUnavailable);
            }

            var backend = ProbeBackend();
            if (backend.Info is not { IsInstalled: true })
            {
                _logger.LogWarning(
                    "Question preview backend unavailable: {BackendType}; reason: {UnavailableReason}",
                    backend.Info?.Type,
                    backend.Info?.UnavailableReason ?? "unsupported platform");
                return _descriptor = QuestionPreviewHostDescriptor.Unavailable(
                    QuestionPreviewAvailability.BackendUnavailable,
                    backend.Requirement);
            }

            try
            {
                _contentServer = new QuestionPreviewContentServer(assetsDirectory, _logger);
                _logger.LogInformation(
                    "Question preview backend available: {BackendType} {BackendVersion}",
                    backend.Info.Type,
                    backend.Info.Version);
                return _descriptor = QuestionPreviewHostDescriptor.Available(_contentServer.Source);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                _logger.LogWarning(exception, "Question preview application origin could not be started");
                return _descriptor = QuestionPreviewHostDescriptor.Unavailable(
                    QuestionPreviewAvailability.BackendUnavailable,
                    backend.Requirement);
            }
        }
    }

    public IQuestionPreviewSession CreateSession()
    {
        var host = GetHostDescriptor();

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return host.IsAvailable && _contentServer is not null
                ? _contentServer.CreateMediaSession(host)
                : new QuestionPreviewHostSession(host);
        }
    }

    public IMediaPreviewSession CreateSession(MediaPreviewSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var host = GetHostDescriptor();

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return host.IsAvailable && _contentServer is not null
                ? _contentServer.CreatePlaybackSession(host, source)
                : new UnavailablePlaybackSession(host.Availability, host.BackendRequirement);
        }
    }

    private static BackendProbe ProbeBackend()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var wpe = WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WpeWebKit);
                if (wpe.IsInstalled)
                {
                    return new BackendProbe(wpe, QuestionPreviewBackendRequirement.LinuxWebKit);
                }

                var gtk = WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebKitGtk);
                return new BackendProbe(gtk, QuestionPreviewBackendRequirement.LinuxWebKit);
            }

            if (OperatingSystem.IsWindows())
            {
                return new BackendProbe(
                    WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebView2),
                    QuestionPreviewBackendRequirement.WindowsWebView2);
            }

            if (OperatingSystem.IsMacOS())
            {
                return new BackendProbe(
                    WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WkWebView),
                    QuestionPreviewBackendRequirement.None);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or BadImageFormatException
            or EntryPointNotFoundException
            or TypeInitializationException)
        {
            return new BackendProbe(
                new DetailedWebViewAdapterInfo(
                    WebViewAdapterType.Unknown,
                    WebViewEngine.Unknown,
                    Version: null,
                    IsSupported: true,
                    IsInstalled: false,
                    UnavailableReason: exception.GetType().Name,
                    SupportedScenarios: WebViewEmbeddingScenario.None),
                GetPlatformRequirement());
        }

        return new BackendProbe(null, QuestionPreviewBackendRequirement.UnsupportedPlatform);
    }

    private static QuestionPreviewBackendRequirement GetPlatformRequirement() =>
        OperatingSystem.IsLinux()
            ? QuestionPreviewBackendRequirement.LinuxWebKit
            : OperatingSystem.IsWindows()
                ? QuestionPreviewBackendRequirement.WindowsWebView2
                : QuestionPreviewBackendRequirement.UnsupportedPlatform;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _contentServer?.Dispose();
            _contentServer = null;
        }
    }

    private sealed record BackendProbe(
        DetailedWebViewAdapterInfo? Info,
        QuestionPreviewBackendRequirement Requirement);

    private sealed class UnavailablePlaybackSession(
        QuestionPreviewAvailability availability,
        QuestionPreviewBackendRequirement backendRequirement) : IMediaPreviewSession
    {
        public Uri? Source => null;

        public QuestionPreviewAvailability Availability { get; } = availability;

        public QuestionPreviewBackendRequirement BackendRequirement { get; } = backendRequirement;

        public void Dispose() { }
    }
}
