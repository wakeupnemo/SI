using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SIQuester.Avalonia.Views;
using SIQuester.Desktop.Services;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Workspaces.Dialogs.Play;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SIQuester.Avalonia.Tests;

[TestFixture]
internal sealed class QuestionPreviewWebBridgeTests
{
    [Test]
    public void HostDispatchScript_EncodesJsonAsDataRatherThanExecutableSource()
    {
        var message = QuestionPreviewProtocol.Serialize(
            new QuestionPreviewRightAnswerMessage("Ответ \"</script><script>window.evil()</script>"));
        var script = QuestionPreviewWebBridge.BuildHostDispatchScript(message);

        Assert.Multiple(() =>
        {
            Assert.That(script, Is.EqualTo(
                "window.siquesterReceiveHostMessage(" + JsonSerializer.Serialize(message) + ");"));
            Assert.That(script, Does.Not.Contain("</script>"));
            Assert.Catch<JsonException>(() => QuestionPreviewWebBridge.BuildHostDispatchScript("not json"));
            Assert.Throws<ArgumentException>(() => QuestionPreviewWebBridge.BuildHostDispatchScript("[]"));
        });
    }

    [Test]
    public void NavigationPolicy_AllowsOnlyTheRandomizedApplicationAssetPath()
    {
        var source = new Uri("http://127.0.0.1:52139/0123456789abcdef/index.html");

        Assert.Multiple(() =>
        {
            Assert.That(QuestionPreviewWebBridge.IsAllowedNavigation(
                source,
                new Uri("http://127.0.0.1:52139/0123456789abcdef/main.js")), Is.True);
            Assert.That(QuestionPreviewWebBridge.IsAllowedNavigation(source, source), Is.True);
            Assert.That(QuestionPreviewWebBridge.IsAllowedNavigation(
                source,
                new Uri("http://127.0.0.1:52139/other/index.html")), Is.False);
            Assert.That(QuestionPreviewWebBridge.IsAllowedNavigation(
                source,
                new Uri("http://localhost:52139/0123456789abcdef/index.html")), Is.False);
            Assert.That(QuestionPreviewWebBridge.IsAllowedNavigation(
                source,
                new Uri("https://example.invalid/0123456789abcdef/index.html")), Is.False);
            Assert.That(QuestionPreviewWebBridge.IsAllowedNavigation(
                source,
                new Uri("http://127.0.0.1:52139/0123456789abcdef/index.html?leak=1")), Is.False);
            Assert.That(QuestionPreviewWebBridge.IsAllowedNavigation(source, null), Is.False);
        });
    }

    [Test]
    public void InboundMessageValidation_IsObjectOnlyAndBounded()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestionPreviewWebBridge.TryValidateInboundMessage(
                "{\"type\":\"ready\",\"value\":\"例\"}",
                out var accepted), Is.True);
            Assert.That(accepted, Is.EqualTo("{\"type\":\"ready\",\"value\":\"例\"}"));
            Assert.That(QuestionPreviewWebBridge.IsReadyMessage(
                "{\"type\":\"siquesterBridgeReady\"}"), Is.True);
            Assert.That(QuestionPreviewWebBridge.IsReadyMessage(accepted), Is.False);
            Assert.That(QuestionPreviewWebBridge.IsAudioUnlockedMessage(
                "{\"type\":\"siquesterAudioUnlocked\"}"), Is.True);
            Assert.That(QuestionPreviewWebBridge.IsAudioUnlockedMessage(accepted), Is.False);
            Assert.That(QuestionPreviewWebBridge.TryValidateInboundMessage("[]", out _), Is.False);
            Assert.That(QuestionPreviewWebBridge.TryValidateInboundMessage("not-json", out _), Is.False);
            Assert.That(QuestionPreviewWebBridge.TryValidateInboundMessage(
                new string('x', QuestionPreviewWebBridge.MaxInboundMessageLength + 1),
                out _), Is.False);
        });
    }

    [Test]
    public async Task LoopbackOrigin_ServesOnlyBoundedApplicationAssetsWithRestrictiveHeaders()
    {
        var assetsDirectory = Path.Combine(
            Path.GetTempPath(),
            "siquester-preview-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assetsDirectory);

        try
        {
            foreach (var name in new[]
            {
                "index.html",
                "main.js",
                "vendor.js",
                "script.js",
                "siquester-bridge.js",
                "style.css",
                "media-preview.html",
                "media-preview.js",
            })
            {
                await File.WriteAllTextAsync(Path.Combine(assetsDirectory, name), "asset:" + name);
            }

            using var server = new QuestionPreviewContentServer(
                assetsDirectory,
                NullLogger<QuestionPreviewContentServer>.Instance);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(server.Source);
            var content = await response.Content.ReadAsStringAsync();
            using var traversal = await client.GetAsync(new Uri(server.Source, "../index.html"));
            using var query = await client.GetAsync(new Uri(server.Source + "?unexpected=1"));
            using var wrongHostRequest = new HttpRequestMessage(HttpMethod.Get, server.Source);
            wrongHostRequest.Headers.Host = "example.invalid";
            using var wrongHost = await client.SendAsync(wrongHostRequest);
            var oversizedRequestLineStatus = await SendRawRequestAsync(
                server.Source,
                $"GET /{new string('x', 5000)} HTTP/1.1\r\nHost: {server.Source.Authority}\r\n\r\n");
            var oversizedHeaderStatus = await SendRawRequestAsync(
                server.Source,
                $"GET {server.Source.PathAndQuery} HTTP/1.1\r\nHost: {server.Source.Authority}\r\nX-Oversized: {new string('x', 17000)}\r\n\r\n");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(content, Is.EqualTo("asset:index.html"));
                Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
                Assert.That(response.Headers.TryGetValues("Content-Security-Policy", out var values), Is.True);
                Assert.That(values!.Single(), Does.Contain("default-src 'self'"));
                Assert.That(values!.Single(), Does.Contain("media-src 'self' data: blob:"));
                Assert.That(values!.Single(), Does.Contain("frame-src 'none'"));
                Assert.That(response.Headers.TryGetValues("X-Content-Type-Options", out var contentTypeValues), Is.True);
                Assert.That(contentTypeValues!.Single(), Is.EqualTo("nosniff"));
                Assert.That(traversal.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(query.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(wrongHost.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(oversizedRequestLineStatus, Does.StartWith("HTTP/1.1 400 "));
                Assert.That(oversizedHeaderStatus, Does.StartWith("HTTP/1.1 431 "));
            });
        }
        finally
        {
            Directory.Delete(assetsDirectory, recursive: true);
        }
    }

    private static async Task<string> SendRawRequestAsync(Uri source, string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(source.Host, source.Port);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        return await reader.ReadLineAsync() ?? string.Empty;
    }

    [Test]
    public async Task LoopbackMediaSession_UsesOpaqueUrlsRangesAndDeterministicRemoval()
    {
        var assetsDirectory = Path.Combine(
            Path.GetTempPath(),
            "siquester-preview-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assetsDirectory);
        var bytes = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var disposedStreamCount = 0;

        try
        {
            foreach (var name in new[]
            {
                "index.html",
                "main.js",
                "vendor.js",
                "script.js",
                "siquester-bridge.js",
                "style.css",
                "media-preview.html",
                "media-preview.js",
            })
            {
                await File.WriteAllTextAsync(Path.Combine(assetsDirectory, name), "asset:" + name);
            }

            using var server = new QuestionPreviewContentServer(
                assetsDirectory,
                NullLogger<QuestionPreviewContentServer>.Instance);
            var host = QuestionPreviewHostDescriptor.Available(server.Source);
            using var session = server.CreateMediaSession(host);
            var source = new QuestionPreviewMediaSource(
                QuestionPreviewMediaKind.Image,
                "секретное имя 例.png",
                () => new QuestionPreviewMediaStream(
                    new NonSeekableReadStream(bytes, () => disposedStreamCount++),
                    bytes.Length));

            Assert.That(session.TryGetMediaSource(source, out var mediaSource), Is.True);
            Assert.That(session.TryGetMediaSource(source, out var repeatedSource), Is.True);
            Assert.That(repeatedSource, Is.EqualTo(mediaSource));
            Assert.That(mediaSource, Does.Not.Contain(Uri.EscapeDataString(source.Name)));
            Assert.That(session.TryGetMediaSource(
                new QuestionPreviewMediaSource(
                    QuestionPreviewMediaKind.Image,
                    "unsafe.svg",
                    () => new QuestionPreviewMediaStream(new MemoryStream(bytes), bytes.Length)),
                out _), Is.False);
            for (var index = 0; index < 255; index++)
            {
                Assert.That(session.TryGetMediaSource(
                    new QuestionPreviewMediaSource(
                        QuestionPreviewMediaKind.Image,
                        $"bounded-{index}.png",
                        () => new QuestionPreviewMediaStream(new MemoryStream(bytes), bytes.Length)),
                    out _), Is.True);
            }

            Assert.That(session.TryGetMediaSource(
                new QuestionPreviewMediaSource(
                    QuestionPreviewMediaKind.Image,
                    "over-limit.png",
                    () => new QuestionPreviewMediaStream(new MemoryStream(bytes), bytes.Length)),
                out _), Is.False);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var full = await client.GetAsync(mediaSource);
            var fullBytes = await full.Content.ReadAsByteArrayAsync();
            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, mediaSource);
            rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=5-11");
            using var range = await client.SendAsync(rangeRequest);
            var rangeBytes = await range.Content.ReadAsByteArrayAsync();
            using var invalidRangeRequest = new HttpRequestMessage(HttpMethod.Get, mediaSource);
            invalidRangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=200-300");
            using var invalidRange = await client.SendAsync(invalidRangeRequest);

            Assert.Multiple(() =>
            {
                Assert.That(full.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(full.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
                Assert.That(full.Headers.AcceptRanges, Does.Contain("bytes"));
                Assert.That(fullBytes, Is.EqualTo(bytes));
                Assert.That(range.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
                Assert.That(range.Content.Headers.ContentRange?.ToString(), Is.EqualTo("bytes 5-11/32"));
                Assert.That(rangeBytes, Is.EqualTo(bytes[5..12]));
                Assert.That(invalidRange.StatusCode, Is.EqualTo(HttpStatusCode.RequestedRangeNotSatisfiable));
                Assert.That(disposedStreamCount, Is.EqualTo(3));
            });

            session.Dispose();
            using var removed = await client.GetAsync(mediaSource);
            Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
        finally
        {
            Directory.Delete(assetsDirectory, recursive: true);
        }
    }

    [Test]
    public async Task LoopbackPlaybackSession_UsesApplicationPageAndRemovesOpaqueAudioRoute()
    {
        var assetsDirectory = Path.Combine(
            Path.GetTempPath(),
            "siquester-media-preview-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assetsDirectory);
        var bytes = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();

        try
        {
            foreach (var name in new[]
            {
                "index.html",
                "main.js",
                "vendor.js",
                "script.js",
                "siquester-bridge.js",
                "style.css",
                "media-preview.html",
                "media-preview.js",
            })
            {
                await File.WriteAllTextAsync(Path.Combine(assetsDirectory, name), "asset:" + name);
            }

            using var server = new QuestionPreviewContentServer(
                assetsDirectory,
                NullLogger<QuestionPreviewContentServer>.Instance);
            var host = QuestionPreviewHostDescriptor.Available(server.Source);
            var session = server.CreatePlaybackSession(
                host,
                new MediaPreviewSource(
                    MediaPreviewKind.Audio,
                    "секретное имя 例.wav",
                    () => new QuestionPreviewMediaStream(new MemoryStream(bytes), bytes.Length)));

            Assert.That(session.IsAvailable, Is.True);
            Assert.That(session.Source!.AbsolutePath, Does.EndWith("/media-preview.html"));
            Assert.That(session.Source.AbsoluteUri, Does.Not.Contain(Uri.EscapeDataString("секретное имя 例.wav")));
            var fragment = session.Source.Fragment.TrimStart('#');
            Assert.That(fragment, Does.StartWith("audio="));
            var mediaSource = Uri.UnescapeDataString(fragment["audio=".Length..]);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var page = await client.GetAsync(session.Source);
            using var media = await client.GetAsync(mediaSource);
            Assert.Multiple(() =>
            {
                Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(media.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(media.Content.Headers.ContentType?.MediaType, Is.EqualTo("audio/wav"));
            });

            session.Dispose();
            using var removed = await client.GetAsync(mediaSource);
            Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
        finally
        {
            Directory.Delete(assetsDirectory, recursive: true);
        }
    }

    [Test]
    [NonParallelizable]
    public async Task DesktopPreviewService_ReportsNativeBackendAndServesRetainedPlayerWhenAvailable()
    {
        using var service = new DesktopQuestionPreviewService(
            NullLogger<DesktopQuestionPreviewService>.Instance);
        var descriptor = service.GetHostDescriptor();
        var expectedBackend = Environment.GetEnvironmentVariable("SIQUESTER_EXPECT_PREVIEW_BACKEND");

        if (string.Equals(expectedBackend, "available", StringComparison.Ordinal))
        {
            Assert.That(descriptor.IsAvailable, Is.True,
                "The native preview runtime was required for this test invocation.");
        }
        else if (string.Equals(expectedBackend, "unavailable", StringComparison.Ordinal))
        {
            Assert.That(descriptor.IsAvailable, Is.False,
                "The missing-backend path was required for this test invocation.");
        }

        if (!descriptor.IsAvailable)
        {
            using var unavailableSession = service.CreateSession();
            var unavailableMedia = new QuestionPreviewMediaSource(
                QuestionPreviewMediaKind.Image,
                "unavailable.png",
                () => new QuestionPreviewMediaStream(new MemoryStream([1, 2, 3]), 3));

            Assert.Multiple(() =>
            {
                Assert.That(descriptor.Availability,
                    Is.EqualTo(QuestionPreviewAvailability.BackendUnavailable));
                Assert.That(descriptor.ApplicationSource, Is.Null);
                Assert.That(descriptor.BackendRequirement,
                    Is.EqualTo(OperatingSystem.IsLinux()
                        ? QuestionPreviewBackendRequirement.LinuxWebKit
                        : OperatingSystem.IsWindows()
                            ? QuestionPreviewBackendRequirement.WindowsWebView2
                            : OperatingSystem.IsMacOS()
                                ? QuestionPreviewBackendRequirement.None
                                : QuestionPreviewBackendRequirement.UnsupportedPlatform));
                Assert.That(unavailableSession.Host, Is.SameAs(descriptor));
                Assert.That(unavailableSession.TryGetMediaSource(unavailableMedia, out _), Is.False);
            });
            return;
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var html = await client.GetStringAsync(descriptor.ApplicationSource);
        var bridge = await client.GetStringAsync(new Uri(descriptor.ApplicationSource!, "siquester-bridge.js"));
        using var availableSession = service.CreateSession();
        var mediaBytes = new byte[] { 7, 6, 5, 4 };
        var availableMedia = new QuestionPreviewMediaSource(
            QuestionPreviewMediaKind.Image,
            "available.png",
            () => new QuestionPreviewMediaStream(new MemoryStream(mediaBytes), mediaBytes.Length));
        var mediaRegistered = availableSession.TryGetMediaSource(availableMedia, out var mediaSource);
        var servedMedia = mediaRegistered
            ? await client.GetByteArrayAsync(mediaSource)
            : Array.Empty<byte>();

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.ApplicationSource!.IsLoopback, Is.True);
            Assert.That(html, Does.Contain("siquester-bridge.js"));
            Assert.That(bridge, Does.Contain("window.siquesterReceiveHostMessage"));
            Assert.That(bridge, Does.Contain("window.siquesterBridgeReady = true"));
            Assert.That(bridge, Does.Contain("siquester-audio-unlock"));
            Assert.That(bridge, Does.Contain("new Audio(item.value)"));
            Assert.That(availableSession.Host, Is.SameAs(descriptor));
            Assert.That(mediaRegistered, Is.True);
            Assert.That(servedMedia, Is.EqualTo(mediaBytes));
        });
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly Action _onDispose;
        private bool _disposed;

        public NonSeekableReadStream(byte[] content, Action onDispose)
        {
            _inner = new MemoryStream(content, writable: false);
            _onDispose = onDispose;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _inner.Dispose();
                _onDispose();
            }

            base.Dispose(disposing);
        }
    }
}
