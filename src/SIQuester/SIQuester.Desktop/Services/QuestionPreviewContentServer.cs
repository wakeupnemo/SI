using Microsoft.Extensions.Logging;
using SIQuester.ViewModel.Contracts;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SIQuester.Desktop.Services;

/// <summary>
/// Serves only the application-owned question-player assets from an unguessable loopback path.
/// </summary>
internal sealed class QuestionPreviewContentServer : IDisposable
{
    private const int MaxAssetCount = 128;
    private const int MaxMediaCountPerSession = 256;
    private const int MaxConcurrentRequests = 16;
    private const long MaxAssetLength = 16 * 1024 * 1024;
    private const long MaxTotalAssetLength = 32 * 1024 * 1024;
    private const long MaxMediaLength = 512L * 1024 * 1024;
    private const int MaxRequestLineLength = 4096;
    private const int MaxHeaderLength = 16 * 1024;
    private static readonly string[] RequiredAssetNames =
        ["index.html", "main.js", "vendor.js", "script.js", "siquester-bridge.js", "style.css"];
    private static readonly HashSet<string> AllowedExtensions = new(
        [".html", ".js", ".css", ".ttf", ".woff", ".woff2", ".png", ".jpg", ".jpeg", ".gif", ".svg"],
        StringComparer.OrdinalIgnoreCase);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _requestSlots = new(MaxConcurrentRequests, MaxConcurrentRequests);
    private readonly Lock _requestSync = new();
    private readonly HashSet<Task> _requestTasks = [];
    private readonly IReadOnlyDictionary<string, Asset> _assets;
    private readonly ConcurrentDictionary<string, PreviewMediaAsset> _mediaAssets = new(StringComparer.Ordinal);
    private readonly ILogger _logger;
    private readonly string _routePrefix;
    private readonly string _expectedHost;
    private readonly Task _acceptLoop;
    private volatile bool _disposed;

    public QuestionPreviewContentServer(string assetsDirectory, ILogger logger)
    {
        _logger = logger;
        _assets = LoadAssetManifest(assetsDirectory);
        _routePrefix = "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + "/";
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(16);
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Source = new Uri($"http://127.0.0.1:{port}{_routePrefix}index.html");
        _expectedHost = Source.Authority;
        _acceptLoop = AcceptLoopAsync(_lifetime.Token);
    }

    public Uri Source { get; }

    public IQuestionPreviewSession CreateMediaSession(QuestionPreviewHostDescriptor host)
    {
        ArgumentNullException.ThrowIfNull(host);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!host.IsAvailable || host.ApplicationSource != Source)
        {
            throw new ArgumentException("The preview host does not belong to this loopback origin.", nameof(host));
        }

        return new MediaSession(this, host);
    }

    public static bool HasRequiredAssets(string assetsDirectory)
    {
        if (!Directory.Exists(assetsDirectory))
        {
            return false;
        }

        return RequiredAssetNames.All(name => File.Exists(Path.Combine(assetsDirectory, name)));
    }

    private static IReadOnlyDictionary<string, Asset> LoadAssetManifest(string assetsDirectory)
    {
        if (!HasRequiredAssets(assetsDirectory))
        {
            throw new InvalidOperationException("Question-preview assets are incomplete.");
        }

        var assets = new Dictionary<string, Asset>(StringComparer.Ordinal);
        long totalLength = 0;

        foreach (var path in Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var file = new FileInfo(path);
            if (!AllowedExtensions.Contains(file.Extension) || file.LinkTarget is not null)
            {
                continue;
            }

            if (file.Length < 0 || file.Length > MaxAssetLength)
            {
                throw new InvalidOperationException("A question-preview asset exceeds the allowed size.");
            }

            totalLength = checked(totalLength + file.Length);
            if (totalLength > MaxTotalAssetLength || assets.Count >= MaxAssetCount)
            {
                throw new InvalidOperationException("Question-preview assets exceed the allowed manifest bounds.");
            }

            assets.Add(file.Name, new Asset(file.FullName, file.Length, GetContentType(file.Extension)));
        }

        if (RequiredAssetNames.Any(name => !assets.ContainsKey(name)))
        {
            throw new InvalidOperationException("Question-preview assets are incomplete or unsafe.");
        }

        return assets;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _requestSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    _requestSlots.Release();
                    throw;
                }

                var requestTask = HandleClientSafelyAsync(client, cancellationToken);

                lock (_requestSync)
                {
                    _requestTasks.Add(requestTask);
                }

                _ = ObserveRequestAsync(requestTask);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Question preview loopback origin stopped unexpectedly");
        }
    }

    private async Task ObserveRequestAsync(Task requestTask)
    {
        try
        {
            await requestTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Question preview request task failed unexpectedly");
        }
        finally
        {
            lock (_requestSync)
            {
                _requestTasks.Remove(requestTask);
            }

            _requestSlots.Release();
        }
    }

    private async Task HandleClientSafelyAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(15));

            try
            {
                client.NoDelay = true;
                await HandleClientAsync(client, requestTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException exception)
            {
                _logger.LogDebug(exception, "Question preview request was cancelled or timed out");
            }
            catch (IOException exception)
            {
                _logger.LogDebug(exception, "Question preview asset request ended before completion");
            }
            catch (SocketException exception)
            {
                _logger.LogDebug(exception, "Question preview asset connection ended before completion");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Question preview asset request failed");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        var requestLineResult = await ReadAsciiLineAsync(stream, MaxRequestLineLength, cancellationToken)
            .ConfigureAwait(false);
        var requestLine = requestLineResult.Value;

        if (requestLineResult.ExceededLimit || requestLine is null)
        {
            await WriteStatusAsync(stream, 400, "Bad Request", cancellationToken);
            return;
        }

        var headerLength = 0;
        string? rangeHeader = null;
        string? hostHeader = null;
        while (true)
        {
            var headerResult = await ReadAsciiLineAsync(
                stream,
                MaxHeaderLength - headerLength,
                cancellationToken).ConfigureAwait(false);
            if (headerResult.ExceededLimit)
            {
                await WriteStatusAsync(stream, 431, "Request Header Fields Too Large", cancellationToken);
                return;
            }

            var header = headerResult.Value;
            if (header is null)
            {
                await WriteStatusAsync(stream, 400, "Bad Request", cancellationToken);
                return;
            }

            if (header.Length == 0)
            {
                break;
            }

            headerLength = checked(headerLength + header.Length);

            var separator = header.IndexOf(':');
            if (separator <= 0)
            {
                await WriteStatusAsync(stream, 400, "Bad Request", cancellationToken);
                return;
            }

            if (header.AsSpan(0, separator).Trim().Equals("Range", StringComparison.OrdinalIgnoreCase))
            {
                if (rangeHeader is not null)
                {
                    await WriteStatusAsync(stream, 400, "Bad Request", cancellationToken);
                    return;
                }

                rangeHeader = header[(separator + 1)..].Trim();
            }
            else if (header.AsSpan(0, separator).Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                if (hostHeader is not null)
                {
                    await WriteStatusAsync(stream, 400, "Bad Request", cancellationToken);
                    return;
                }

                hostHeader = header[(separator + 1)..].Trim();
            }
        }

        var requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length != 3 || requestParts[2] is not ("HTTP/1.0" or "HTTP/1.1"))
        {
            await WriteStatusAsync(stream, 400, "Bad Request", cancellationToken);
            return;
        }

        if (requestParts[2] == "HTTP/1.1"
            && !string.Equals(hostHeader, _expectedHost, StringComparison.OrdinalIgnoreCase))
        {
            await WriteStatusAsync(stream, 400, "Bad Request", cancellationToken);
            return;
        }

        var isHead = requestParts[0] == "HEAD";
        if (!isHead && requestParts[0] != "GET")
        {
            await WriteStatusAsync(stream, 405, "Method Not Allowed", cancellationToken);
            return;
        }

        var requestPath = requestParts[1];
        if (!requestPath.StartsWith(_routePrefix, StringComparison.Ordinal)
            || requestPath.Contains('?')
            || requestPath.Contains('#'))
        {
            await WriteStatusAsync(stream, 404, "Not Found", cancellationToken);
            return;
        }

        if (_mediaAssets.TryGetValue(requestPath, out var mediaAsset))
        {
            await WriteMediaAsync(stream, mediaAsset, rangeHeader, isHead, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var assetName = requestPath[_routePrefix.Length..];
        if (assetName.Length == 0 || assetName.Contains('/') || assetName.Contains('\\')
            || !_assets.TryGetValue(assetName, out var asset))
        {
            await WriteStatusAsync(stream, 404, "Not Found", cancellationToken);
            return;
        }

        var headerBytes = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\n" +
            $"Content-Type: {asset.ContentType}\r\n" +
            $"Content-Length: {asset.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; media-src 'self' data: blob:; connect-src 'none'; object-src 'none'; frame-src 'none'; base-uri 'none'; form-action 'none'\r\n" +
            "Referrer-Policy: no-referrer\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "X-Frame-Options: DENY\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(headerBytes, cancellationToken);

        if (!isHead)
        {
            await using var file = new FileStream(
                asset.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await file.CopyToAsync(stream, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    private static async ValueTask<LineReadResult> ReadAsciiLineAsync(
        Stream stream,
        int maxLength,
        CancellationToken cancellationToken)
    {
        if (maxLength < 0)
        {
            return new LineReadResult(null, true);
        }

        var bytes = new List<byte>(Math.Min(maxLength, 256));
        var singleByte = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(singleByte, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Count == 0
                    ? new LineReadResult(null, false)
                    : new LineReadResult(Encoding.ASCII.GetString([.. bytes]), false);
            }

            if (singleByte[0] == (byte)'\n')
            {
                if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
                {
                    bytes.RemoveAt(bytes.Count - 1);
                }

                return new LineReadResult(Encoding.ASCII.GetString([.. bytes]), false);
            }

            if (bytes.Count >= maxLength)
            {
                return new LineReadResult(null, true);
            }

            bytes.Add(singleByte[0]);
        }
    }

    private async Task WriteMediaAsync(
        Stream response,
        PreviewMediaAsset asset,
        string? rangeHeader,
        bool isHead,
        CancellationToken serverCancellationToken)
    {
        if (!asset.SessionLifetime.TryAcquire(out var sessionCancellationToken))
        {
            await WriteStatusAsync(response, 404, "Not Found", serverCancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                serverCancellationToken,
                sessionCancellationToken);
            var cancellationToken = requestCancellation.Token;
            cancellationToken.ThrowIfCancellationRequested();

            QuestionPreviewMediaStream? media;
            try
            {
                media = asset.Source.OpenRead();
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ObjectDisposedException)
            {
                _logger.LogWarning(exception, "Question preview package media could not be opened");
                await WriteStatusAsync(response, 404, "Not Found", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (media is null)
            {
                await WriteStatusAsync(response, 404, "Not Found", cancellationToken).ConfigureAwait(false);
                return;
            }

            using (media)
            {
                if (media.Length > MaxMediaLength)
                {
                    await WriteStatusAsync(response, 413, "Content Too Large", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!TryResolveRange(rangeHeader, media.Length, out var range, out var isPartial))
                {
                    await WriteRangeNotSatisfiableAsync(response, media.Length, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var contentLength = range.Length;
                var status = isPartial ? "206 Partial Content" : "200 OK";
                var contentRange = isPartial
                    ? $"Content-Range: bytes {range.Start}-{range.End}/{media.Length}\r\n"
                    : string.Empty;
                var headerBytes = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\n" +
                    $"Content-Type: {asset.ContentType}\r\n" +
                    $"Content-Length: {contentLength}\r\n" +
                    contentRange +
                    "Accept-Ranges: bytes\r\n" +
                    "Cache-Control: no-store\r\n" +
                    "Content-Security-Policy: default-src 'none'; sandbox\r\n" +
                    "Referrer-Policy: no-referrer\r\n" +
                    "X-Content-Type-Options: nosniff\r\n" +
                    "Content-Disposition: inline\r\n" +
                    "Connection: close\r\n\r\n");
                await response.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

                if (!isHead && contentLength > 0)
                {
                    await PositionStreamAsync(media.Stream, range.Start, cancellationToken).ConfigureAwait(false);
                    await CopyExactlyAsync(media.Stream, response, contentLength, cancellationToken).ConfigureAwait(false);
                }

                await response.FlushAsync(cancellationToken).ConfigureAwait(false);

                if (!isHead && asset.TryMarkFirstSuccessfulBody())
                {
                    _logger.LogInformation(
                        "Question preview package media served: {MediaKind}, {MediaLength} bytes",
                        asset.Source.Kind,
                        media.Length);
                }
            }
        }
        finally
        {
            asset.SessionLifetime.Release();
        }
    }

    private static bool TryResolveRange(
        string? rangeHeader,
        long totalLength,
        out ByteRange range,
        out bool isPartial)
    {
        isPartial = rangeHeader is not null;
        if (rangeHeader is null)
        {
            range = new ByteRange(0, totalLength - 1);
            return true;
        }

        range = default;
        if (totalLength == 0
            || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)
            || rangeHeader.Contains(','))
        {
            return false;
        }

        var value = rangeHeader[6..].Trim();
        var separator = value.IndexOf('-');
        if (separator < 0 || value.IndexOf('-', separator + 1) >= 0)
        {
            return false;
        }

        var startText = value[..separator].Trim();
        var endText = value[(separator + 1)..].Trim();
        long start;
        long end;

        if (startText.Length == 0)
        {
            if (!TryParseNonNegativeInt64(endText, out var suffixLength) || suffixLength <= 0)
            {
                return false;
            }

            start = Math.Max(0, totalLength - suffixLength);
            end = totalLength - 1;
        }
        else
        {
            if (!TryParseNonNegativeInt64(startText, out start) || start >= totalLength)
            {
                return false;
            }

            if (endText.Length == 0)
            {
                end = totalLength - 1;
            }
            else if (!TryParseNonNegativeInt64(endText, out end) || end < start)
            {
                return false;
            }

            end = Math.Min(end, totalLength - 1);
        }

        range = new ByteRange(start, end);
        return true;
    }

    private static bool TryParseNonNegativeInt64(string value, out long result) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static async Task PositionStreamAsync(
        Stream stream,
        long position,
        CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            stream.Seek(position, SeekOrigin.Begin);
            return;
        }

        var buffer = new byte[64 * 1024];
        var remaining = position;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Question-preview media ended before the requested range.");
            }

            remaining -= read;
        }
    }

    private static async Task CopyExactlyAsync(
        Stream source,
        Stream destination,
        long length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var remaining = length;
        while (remaining > 0)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Question-preview media ended before its declared length.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    private static async Task WriteRangeNotSatisfiableAsync(
        Stream stream,
        long totalLength,
        CancellationToken cancellationToken)
    {
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{totalLength}\r\n" +
            "Content-Length: 0\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteStatusAsync(
        Stream stream,
        int statusCode,
        string reason,
        CancellationToken cancellationToken)
    {
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {reason}\r\nContent-Length: 0\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".ttf" => "font/ttf",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    private static bool TryGetMediaContentType(
        QuestionPreviewMediaKind kind,
        string name,
        out string extension,
        out string contentType)
    {
        extension = Path.GetExtension(name).ToLowerInvariant();
        contentType = (kind, extension) switch
        {
            (QuestionPreviewMediaKind.Image, ".jpg" or ".jpe" or ".jpeg") => "image/jpeg",
            (QuestionPreviewMediaKind.Image, ".png") => "image/png",
            (QuestionPreviewMediaKind.Image, ".gif") => "image/gif",
            (QuestionPreviewMediaKind.Image, ".webp") => "image/webp",
            (QuestionPreviewMediaKind.Image, ".avif") => "image/avif",
            (QuestionPreviewMediaKind.Audio, ".mp3") => "audio/mpeg",
            (QuestionPreviewMediaKind.Audio, ".opus") => "audio/ogg",
            (QuestionPreviewMediaKind.Audio, ".ogg") => "audio/ogg",
            (QuestionPreviewMediaKind.Audio, ".wav") => "audio/wav",
            (QuestionPreviewMediaKind.Audio, ".m4a") => "audio/mp4",
            (QuestionPreviewMediaKind.Audio, ".aac") => "audio/aac",
            (QuestionPreviewMediaKind.Audio, ".flac") => "audio/flac",
            (QuestionPreviewMediaKind.Video, ".mp4") => "video/mp4",
            (QuestionPreviewMediaKind.Video, ".webm") => "video/webm",
            (QuestionPreviewMediaKind.Video, ".ogv") => "video/ogg",
            (QuestionPreviewMediaKind.Video, ".mov") => "video/quicktime",
            _ => string.Empty,
        };

        return contentType.Length > 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _listener.Stop();
        _acceptLoop.GetAwaiter().GetResult();

        foreach (var sessionLifetime in _mediaAssets.Values
            .Select(asset => asset.SessionLifetime)
            .Distinct())
        {
            sessionLifetime.Dispose();
        }

        _mediaAssets.Clear();

        Task[] activeRequests;
        lock (_requestSync)
        {
            activeRequests = [.. _requestTasks];
        }

        Task.WhenAll(activeRequests).GetAwaiter().GetResult();
        _requestSlots.Dispose();
        _lifetime.Dispose();
    }

    private sealed class MediaSession : IQuestionPreviewSession
    {
        private readonly QuestionPreviewContentServer _server;
        private readonly Lock _sync = new();
        private readonly MediaSessionLifetime _lifetime = new();
        private readonly Dictionary<MediaKey, string> _sources = [];
        private readonly List<string> _paths = [];
        private readonly string _sessionSegment = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        private bool _disposed;

        public MediaSession(QuestionPreviewContentServer server, QuestionPreviewHostDescriptor host)
        {
            _server = server;
            Host = host;
            _server._logger.LogInformation("Question preview media session created");
        }

        public QuestionPreviewHostDescriptor Host { get; }

        public bool TryGetMediaSource(QuestionPreviewMediaSource media, out string source)
        {
            ArgumentNullException.ThrowIfNull(media);

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                ObjectDisposedException.ThrowIf(_server._disposed, _server);

                var key = new MediaKey(media.Kind, media.Name);
                if (_sources.TryGetValue(key, out source!))
                {
                    return true;
                }

                if (_sources.Count >= MaxMediaCountPerSession)
                {
                    source = string.Empty;
                    return false;
                }

                if (!TryGetMediaContentType(media.Kind, media.Name, out var extension, out var contentType))
                {
                    source = string.Empty;
                    return false;
                }

                var mediaSegment = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                var path = $"{_server._routePrefix}media/{_sessionSegment}/{mediaSegment}{extension}";
                var asset = new PreviewMediaAsset(media, contentType, _lifetime);
                if (!_server._mediaAssets.TryAdd(path, asset))
                {
                    source = string.Empty;
                    return false;
                }

                source = new Uri(_server.Source, path).AbsoluteUri;
                _sources.Add(key, source);
                _paths.Add(path);
                return true;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _lifetime.Dispose();

                foreach (var path in _paths)
                {
                    _server._mediaAssets.TryRemove(path, out _);
                }

                _paths.Clear();
                _sources.Clear();
                _server._logger.LogInformation("Question preview media session disposed");
            }
        }
    }

    private sealed record Asset(string Path, long Length, string ContentType);

    private sealed class PreviewMediaAsset(
        QuestionPreviewMediaSource source,
        string contentType,
        MediaSessionLifetime sessionLifetime)
    {
        private int _hasLoggedSuccessfulBody;

        public QuestionPreviewMediaSource Source { get; } = source;

        public string ContentType { get; } = contentType;

        public MediaSessionLifetime SessionLifetime { get; } = sessionLifetime;

        public bool TryMarkFirstSuccessfulBody() =>
            Interlocked.Exchange(ref _hasLoggedSuccessfulBody, 1) == 0;
    }

    private sealed class MediaSessionLifetime : IDisposable
    {
        private readonly Lock _sync = new();
        private CancellationTokenSource? _cancellation = new();
        private int _activeRequests;
        private bool _closing;

        public bool TryAcquire(out CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_closing || _cancellation is null)
                {
                    cancellationToken = new CancellationToken(canceled: true);
                    return false;
                }

                _activeRequests++;
                cancellationToken = _cancellation.Token;
                return true;
            }
        }

        public void Release()
        {
            CancellationTokenSource? cancellationToDispose = null;

            lock (_sync)
            {
                if (_activeRequests <= 0)
                {
                    throw new InvalidOperationException("Question-preview media request ownership is unbalanced.");
                }

                _activeRequests--;
                if (_closing && _activeRequests == 0)
                {
                    cancellationToDispose = _cancellation;
                    _cancellation = null;
                }
            }

            cancellationToDispose?.Dispose();
        }

        public void Dispose()
        {
            CancellationTokenSource? cancellationToDispose = null;
            CancellationTokenSource? cancellationToCancel;

            lock (_sync)
            {
                if (_closing)
                {
                    return;
                }

                _closing = true;
                cancellationToCancel = _cancellation;
                if (_activeRequests == 0)
                {
                    cancellationToDispose = _cancellation;
                    _cancellation = null;
                }
            }

            cancellationToCancel?.Cancel();
            cancellationToDispose?.Dispose();
        }
    }

    private readonly record struct MediaKey(QuestionPreviewMediaKind Kind, string Name);

    private readonly record struct ByteRange(long Start, long End)
    {
        public long Length => End - Start + 1;
    }

    private readonly record struct LineReadResult(string? Value, bool ExceededLimit);
}
