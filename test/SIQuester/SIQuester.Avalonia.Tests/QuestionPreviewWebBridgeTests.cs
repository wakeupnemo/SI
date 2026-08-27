using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SIQuester.Avalonia.Views;
using SIQuester.Desktop.Services;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Workspaces.Dialogs.Play;
using System.Net;
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

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(content, Is.EqualTo("asset:index.html"));
                Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
                Assert.That(response.Headers.TryGetValues("Content-Security-Policy", out var values), Is.True);
                Assert.That(values!.Single(), Does.Contain("default-src 'self'"));
                Assert.That(response.Headers.TryGetValues("X-Content-Type-Options", out var contentTypeValues), Is.True);
                Assert.That(contentTypeValues!.Single(), Is.EqualTo("nosniff"));
                Assert.That(traversal.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(query.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
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
            });
            return;
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var html = await client.GetStringAsync(descriptor.ApplicationSource);
        var bridge = await client.GetStringAsync(new Uri(descriptor.ApplicationSource!, "siquester-bridge.js"));

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.ApplicationSource!.IsLoopback, Is.True);
            Assert.That(html, Does.Contain("siquester-bridge.js"));
            Assert.That(bridge, Does.Contain("window.siquesterReceiveHostMessage"));
            Assert.That(bridge, Does.Contain("window.siquesterBridgeReady = true"));
        });
    }
}
