using Microsoft.Extensions.Logging;
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
    private const long MaxAssetLength = 16 * 1024 * 1024;
    private const long MaxTotalAssetLength = 32 * 1024 * 1024;
    private const int MaxRequestLineLength = 4096;
    private const int MaxHeaderLength = 16 * 1024;
    private static readonly string[] RequiredAssetNames =
        ["index.html", "main.js", "vendor.js", "script.js", "siquester-bridge.js", "style.css"];
    private static readonly HashSet<string> AllowedExtensions = new(
        [".html", ".js", ".css", ".ttf", ".woff", ".woff2", ".png", ".jpg", ".jpeg", ".gif", ".svg"],
        StringComparer.OrdinalIgnoreCase);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IReadOnlyDictionary<string, Asset> _assets;
    private readonly ILogger _logger;
    private readonly string _routePrefix;
    private readonly Task _acceptLoop;
    private bool _disposed;

    public QuestionPreviewContentServer(string assetsDirectory, ILogger logger)
    {
        _logger = logger;
        _assets = LoadAssetManifest(assetsDirectory);
        _routePrefix = "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + "/";
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(16);
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Source = new Uri($"http://127.0.0.1:{port}{_routePrefix}index.html");
        _acceptLoop = AcceptLoopAsync(_lifetime.Token);
    }

    public Uri Source { get; }

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
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleClientSafelyAsync(client, cancellationToken);
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

    private async Task HandleClientSafelyAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
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
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        if (requestLine is null || requestLine.Length > MaxRequestLineLength)
        {
            await WriteStatusAsync(stream, 400, "Bad Request", cancellationToken);
            return;
        }

        var headerLength = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } header && header.Length > 0)
        {
            headerLength = checked(headerLength + header.Length);
            if (headerLength > MaxHeaderLength)
            {
                await WriteStatusAsync(stream, 431, "Request Header Fields Too Large", cancellationToken);
                return;
            }
        }

        var requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length != 3 || requestParts[2] is not ("HTTP/1.0" or "HTTP/1.1"))
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
        _lifetime.Dispose();
    }

    private sealed record Asset(string Path, long Length, string ContentType);
}
