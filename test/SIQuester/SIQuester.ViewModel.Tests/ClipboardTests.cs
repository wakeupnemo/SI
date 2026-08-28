using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SIPackages;
using SIPackages.Core;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Services;
using SIQuester.ViewModel.Tests.Helpers;
using SIQuester.ViewModel.Tests.Mocks;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Utils.Commands;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class ClipboardTests
{
    [Test]
    public void ItemPayload_CurrentSchema_RoundTripsWithExplicitEnvelope()
    {
        var item = new InfoOwnerData
        {
            ItemLevel = InfoOwnerData.Level.Question,
            ItemData = "<question price=\"300\" />",
            Images = new Dictionary<string, string> { ["лодка.png"] = "/tmp/media with spaces/image.png" },
            EmbeddedImages = new Dictionary<string, byte[]> { ["лодка.png"] = new byte[] { 1, 2, 3 } },
        };

        var payload = SIQuesterClipboardSerializer.SerializeItem(item);
        var root = JsonNode.Parse(payload)!.AsObject();
        var succeeded = SIQuesterClipboardSerializer.TryDeserializeItem(payload, out var restored);

        Assert.Multiple(() =>
        {
            Assert.That(root["schemaVersion"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(root["kind"]!.GetValue<string>(), Is.EqualTo(SIQuesterClipboardSerializer.ItemKind));
            Assert.That(succeeded, Is.True);
            Assert.That(restored!.ItemLevel, Is.EqualTo(InfoOwnerData.Level.Question));
            Assert.That(restored.Images["лодка.png"], Is.EqualTo("/tmp/media with spaces/image.png"));
            Assert.That(restored.EmbeddedImages["лодка.png"], Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(
                SIQuesterClipboardSerializer.SerializeLegacyItem(item),
                Does.Not.Contain("EmbeddedImages"),
                "The WPF compatibility format must not duplicate embedded media bytes");
        });
    }

    [Test]
    public void ItemPayload_VersionOneEnvelope_RemainsReadable()
    {
        var payload = SIQuesterClipboardSerializer.SerializeItem(new InfoOwnerData
        {
            ItemLevel = InfoOwnerData.Level.Round,
            ItemData = "<round name=\"Legacy v1\" />",
        });
        var root = JsonNode.Parse(payload)!.AsObject();
        root["schemaVersion"] = SIQuesterClipboardSerializer.LegacyItemSchemaVersion;

        var succeeded = SIQuesterClipboardSerializer.TryDeserializeLegacyVersionedItem(
            Encoding.UTF8.GetBytes(root.ToJsonString()),
            out var restored);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(restored!.ItemLevel, Is.EqualTo(InfoOwnerData.Level.Round));
            Assert.That(restored.ItemData, Does.Contain("Legacy v1"));
        });
    }

    [TestCase("../escape.png")]
    [TestCase("..\\escape.png")]
    public void ItemPayload_UnsafeEmbeddedMediaName_IsRejected(string unsafeName)
    {
        var item = new InfoOwnerData
        {
            Images = new Dictionary<string, string> { [unsafeName] = "legacy-path" },
            EmbeddedImages = new Dictionary<string, byte[]> { [unsafeName] = new byte[] { 1 } },
        };

        Assert.That(
            () => SIQuesterClipboardSerializer.SerializeItem(item),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ItemPayload_LegacyJson_RemainsReadable()
    {
        var item = new InfoOwnerData
        {
            ItemLevel = InfoOwnerData.Level.Theme,
            ItemData = "<theme name=\"Legacy\" />",
        };
        var legacy = Encoding.UTF8.GetBytes(SIQuesterClipboardSerializer.SerializeLegacyItem(item));

        var succeeded = SIQuesterClipboardSerializer.TryDeserializeLegacyItem(legacy, out var restored);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(restored!.ItemLevel, Is.EqualTo(InfoOwnerData.Level.Theme));
            Assert.That(restored.ItemData, Does.Contain("Legacy"));
        });
    }

    [Test]
    public void ItemPayload_FutureSchema_IsRejected()
    {
        var payload = SIQuesterClipboardSerializer.SerializeItem(new InfoOwnerData());
        var root = JsonNode.Parse(payload)!.AsObject();
        root["schemaVersion"] = SIQuesterClipboardSerializer.CurrentSchemaVersion + 1;

        var succeeded = SIQuesterClipboardSerializer.TryDeserializeItem(
            Encoding.UTF8.GetBytes(root.ToJsonString()),
            out var restored);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(restored, Is.Null);
        });
    }

    [Test]
    public void PackageInfoPayload_CurrentSchema_RoundTrips()
    {
        var payload = SIQuesterClipboardSerializer.SerializePackageInfo(new
        {
            Name = "Пакет",
            Authors = new[] { "Автор" },
            Difficulty = 7,
        });

        var succeeded = SIQuesterClipboardSerializer.TryDeserializePackageInfo(payload, out var restored);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(restored!["Name"].GetString(), Is.EqualTo("Пакет"));
            Assert.That(restored["Authors"].EnumerateArray().Single().GetString(), Is.EqualTo("Автор"));
            Assert.That(restored["Difficulty"].GetInt32(), Is.EqualTo(7));
        });
    }

    [Test]
    public async Task ClipboardServiceMock_MultiFormatWrite_IsTypedAndDefensive()
    {
        var clipboard = new ClipboardServiceMock();
        var customBytes = new byte[] { 1, 2, 3 };
        await clipboard.WriteAsync(new ClipboardWriteRequest
        {
            Text = "text",
            FilePaths = new[] { "/tmp/файл.siq" },
            ImagePng = new byte[] { 4, 5 },
            CustomData =
            [
                new ClipboardCustomData(SIQuesterClipboardSerializer.ItemFormat, customBytes),
            ],
        });
        customBytes[0] = 99;

        var restoredCustom = await clipboard.ReadCustomDataAsync(SIQuesterClipboardSerializer.ItemFormat);
        var restoredText = await clipboard.ReadTextAsync();
        var restoredFilePaths = await clipboard.ReadFilePathsAsync();
        var restoredImage = await clipboard.ReadImagePngAsync();

        Assert.Multiple(() =>
        {
            Assert.That(restoredText, Is.EqualTo("text"));
            Assert.That(restoredFilePaths, Is.EqualTo(new[] { "/tmp/файл.siq" }));
            Assert.That(restoredImage, Is.EqualTo(new byte[] { 4, 5 }));
            Assert.That(restoredCustom, Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public async Task CopyPasteQuestion_AcrossDocuments_UsesVersionedPayload()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var sourceDocument = TestHelper.CreateSimpleTestPackage();
        using var targetDocument = TestHelper.CreateSimpleTestPackage();
        using var source = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(sourceDocument, "source.siq");
        using var target = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(targetDocument, "target.siq");
        var sourceQuestion = source.Package.Rounds[0].Themes[0].Questions[0];
        var targetTheme = target.Package.Rounds[0].Themes[0];
        source.ActiveNode = sourceQuestion;
        target.ActiveNode = targetTheme;

        await ((IAsyncCommand)source.Copy).ExecuteAsync(null);
        var currentPayload = await serviceProvider.GetRequiredService<IClipboardService>()
            .ReadCustomDataAsync(SIQuesterClipboardSerializer.ItemFormat);
        await serviceProvider.GetRequiredService<IClipboardService>().WriteAsync(new ClipboardWriteRequest
        {
            CustomData =
            [
                new ClipboardCustomData(SIQuesterClipboardSerializer.ItemFormat, currentPayload!),
            ],
        });
        await ((IAsyncCommand)target.Paste).ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(currentPayload, Is.Not.Null);
            Assert.That(targetTheme.Questions, Has.Count.EqualTo(2));
            Assert.That(targetTheme.Questions[1].Model.Price, Is.EqualTo(sourceQuestion.Model.Price));
            Assert.That(targetTheme.Questions[1].Right.Single(), Is.EqualTo("Test answer"));
        });
    }

    [Test]
    public async Task CopyPasteQuestion_WithAllMediaSurvivesSourceCloseAndSafeSaveReload()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var clipboard = serviceProvider.GetRequiredService<IClipboardService>();
        var appPaths = serviceProvider.GetRequiredService<IAppPaths>();
        var sourceDocument = TestHelper.CreateSimpleTestPackage();
        var expectedMedia = new[]
        {
            (ContentTypes.Image, "изображение 例.png", new byte[] { 1, 2, 3, 4 }),
            (ContentTypes.Audio, "звук 例.ogg", new byte[] { 5, 6, 7 }),
            (ContentTypes.Video, "видео 例.webm", new byte[] { 8, 9 }),
            (ContentTypes.Html, "страница 例.html", Encoding.UTF8.GetBytes("<p>clipboard</p>")),
        };

        foreach (var (type, name, bytes) in expectedMedia)
        {
            await using var stream = new MemoryStream(bytes, writable: false);
            await sourceDocument.GetCollection(type).AddFileAsync(name, stream);
            sourceDocument.Package.Rounds[0].Themes[0].Questions[0].Script!.Steps[0]
                .Parameters[StepParameterNames.Content].ContentValue!.Add(new ContentItem
                {
                    Type = type,
                    Value = name,
                    IsRef = true,
                });
        }

        var source = factory.CreateViewModelFor(sourceDocument, "source with media.siq");
        using var targetDocument = TestHelper.CreateSimpleTestPackage();
        using var target = factory.CreateViewModelFor(targetDocument, "target with media.siq");
        source.ActiveNode = source.Package.Rounds[0].Themes[0].Questions[0];
        target.ActiveNode = target.Package.Rounds[0].Themes[0];
        var outputPath = Path.Combine(Path.GetTempPath(), $"SIQuester clipboard media {Guid.NewGuid():N}.siq");
        Exception? pasteError = null;
        target.Error += (exception, _) => pasteError = exception;

        try
        {
            await ((IAsyncCommand)source.Copy).ExecuteAsync(null);
            var versionTwoPayload = await clipboard.ReadCustomDataAsync(SIQuesterClipboardSerializer.ItemFormat);
            Assert.That(versionTwoPayload, Is.Not.Null);
            Assert.That(
                SIQuesterClipboardSerializer.TryDeserializeItem(versionTwoPayload!, out var copiedData),
                Is.True);
            Assert.That(
                new[]
                {
                    copiedData!.EmbeddedImages.Count,
                    copiedData.EmbeddedAudio.Count,
                    copiedData.EmbeddedVideo.Count,
                    copiedData.EmbeddedHtml.Count,
                },
                Is.EqualTo(new[] { 1, 1, 1, 1 }));

            source.Dispose();
            await clipboard.WriteAsync(new ClipboardWriteRequest
            {
                CustomData =
                [
                    new ClipboardCustomData(SIQuesterClipboardSerializer.ItemFormat, versionTwoPayload!),
                ],
            });

            await ((IAsyncCommand)target.Paste).ExecuteAsync(null);
            var pastedQuestion = target.Package.Rounds[0].Themes[0].Questions[^1].Model;
            Assert.Multiple(() =>
            {
                Assert.That(pasteError, Is.Null);
                Assert.That(
                    pastedQuestion.GetContent().Where(item => item.IsRef).Select(item => item.Value),
                    Is.EquivalentTo(expectedMedia.Select(item => item.Item2)));
                Assert.That(target.Images.Files.Select(file => file.Model.Name), Does.Contain("изображение 例.png"));
                Assert.That(target.Audio.Files.Select(file => file.Model.Name), Does.Contain("звук 例.ogg"));
                Assert.That(target.Video.Files.Select(file => file.Model.Name), Does.Contain("видео 例.webm"));
                Assert.That(target.Html.Files.Select(file => file.Model.Name), Does.Contain("страница 例.html"));
                Assert.That(target.Images.HasPendingChanges, Is.True);
                Assert.That(target.Audio.HasPendingChanges, Is.True);
                Assert.That(target.Video.HasPendingChanges, Is.True);
                Assert.That(target.Html.HasPendingChanges, Is.True);
            });
            await target.SaveAsInternalAsync(outputPath);

            using (var packageStream = File.OpenRead(outputPath))
            using (var reloaded = SIDocument.Load(packageStream))
            {
                foreach (var (type, name, bytes) in expectedMedia)
                {
                    var collection = reloaded.GetCollection(type);
                    var streamInfo = collection.GetFile(name);
                    Assert.That(
                        streamInfo,
                        Is.Not.Null,
                        $"Missing pasted {type} media {name}; stored names: {string.Join(", ", collection)}");
                    await using var stream = streamInfo!.Stream;
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer);
                    Assert.That(buffer.ToArray(), Is.EqualTo(bytes), $"Changed pasted {type} media {name}");
                }
            }

            Assert.That(
                Directory.Exists(appPaths.TemporaryMediaDirectory)
                    ? Directory.EnumerateFiles(appPaths.TemporaryMediaDirectory, "clipboard-*").ToArray()
                    : Array.Empty<string>(),
                Is.Empty,
                "Committed clipboard staging must be released");
        }
        finally
        {
            source.Dispose();
            File.Delete(outputPath);
        }
    }

    [Test]
    public async Task CopyWithMedia_RetainsOnlyPublishedLegacyStagingAndReleasesItOnClose()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var clipboard = (ClipboardServiceMock)serviceProvider.GetRequiredService<IClipboardService>();
        var appPaths = serviceProvider.GetRequiredService<IAppPaths>();
        using var sourceDocument = TestHelper.CreateSimpleTestPackage();
        const string mediaName = "clipboard lifetime 例.png";
        await sourceDocument.Images.AddFileAsync(mediaName, new MemoryStream(new byte[] { 1, 2, 3 }));
        sourceDocument.Package.Rounds[0].Themes[0].Questions[0].Script!.Steps[0]
            .Parameters[StepParameterNames.Content].ContentValue!.Add(new ContentItem
            {
                Type = ContentTypes.Image,
                Value = mediaName,
                IsRef = true,
            });
        var source = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(sourceDocument, "clipboard lifetime.siq");
        source.ActiveNode = source.Package.Rounds[0].Themes[0].Questions[0];

        try
        {
            await ((IAsyncCommand)source.Copy).ExecuteAsync(null);
            var legacyPayload = await clipboard.ReadCustomDataAsync(SIQuesterClipboardSerializer.LegacyItemFormat);
            Assert.That(SIQuesterClipboardSerializer.TryDeserializeLegacyItem(legacyPayload!, out var copied), Is.True);
            var publishedPath = copied!.Images[mediaName];

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(publishedPath), Is.True,
                    "the published WPF-compatible clipboard payload must remain readable while its source is open");
                Assert.That(
                    Directory.EnumerateFiles(appPaths.TemporaryMediaDirectory, "clipboard-source-*").ToArray(),
                    Is.EqualTo(new[] { publishedPath }));
            });

            clipboard.WriteException = new InvalidOperationException("Clipboard unavailable");
            await ((IAsyncCommand)source.Copy).ExecuteAsync(null);

            Assert.That(
                Directory.EnumerateFiles(appPaths.TemporaryMediaDirectory, "clipboard-source-*").ToArray(),
                Is.EqualTo(new[] { publishedPath }),
                "a failed replacement must clean its staging without invalidating the last published payload");

            source.Dispose();
            Assert.That(File.Exists(publishedPath), Is.False);
        }
        finally
        {
            source.Dispose();
        }
    }

    [Test]
    public async Task ClosingAfterMediaClipboardWrite_ReleasesUnpublishedStaging()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var clipboard = (ClipboardServiceMock)serviceProvider.GetRequiredService<IClipboardService>();
        var appPaths = serviceProvider.GetRequiredService<IAppPaths>();
        using var sourceDocument = TestHelper.CreateSimpleTestPackage();
        const string mediaName = "closing clipboard 例.png";
        await sourceDocument.Images.AddFileAsync(mediaName, new MemoryStream(new byte[] { 1, 2, 3 }));
        sourceDocument.Package.Rounds[0].Themes[0].Questions[0].Script!.Steps[0]
            .Parameters[StepParameterNames.Content].ContentValue!.Add(new ContentItem
            {
                Type = ContentTypes.Image,
                Value = mediaName,
                IsRef = true,
            });
        var source = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(sourceDocument, "closing clipboard.siq");
        source.ActiveNode = source.Package.Rounds[0].Themes[0].Questions[0];
        clipboard.WriteStored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.WriteReturnRelease = releaseWrite.Task;

        var copyTask = ((IAsyncCommand)source.Copy).ExecuteAsync(null);
        await clipboard.WriteStored.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(
            Directory.EnumerateFiles(appPaths.TemporaryMediaDirectory, "clipboard-source-*").Any(),
            Is.True,
            "the test must reach the accepted-but-unpublished staging phase before closing");

        source.Dispose();
        releaseWrite.TrySetResult();
        await copyTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(
            Directory.EnumerateFiles(appPaths.TemporaryMediaDirectory, "clipboard-source-*").ToArray(),
            Is.Empty);
    }

    [Test]
    public async Task CutQuestion_RemovesSourceOnlyAfterClipboardWriteAndCanPasteToTarget()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var sourceDocument = TestHelper.CreateSimpleTestPackage();
        using var targetDocument = TestHelper.CreateSimpleTestPackage();
        using var source = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(sourceDocument, "source.siq");
        using var target = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(targetDocument, "target.siq");
        var sourceTheme = source.Package.Rounds[0].Themes[0];
        var targetTheme = target.Package.Rounds[0].Themes[0];
        source.ActiveNode = sourceTheme.Questions[0];
        target.ActiveNode = targetTheme;

        await ((IAsyncCommand)source.Cut).ExecuteAsync(null);
        await ((IAsyncCommand)target.Paste).ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(sourceTheme.Questions, Is.Empty);
            Assert.That(targetTheme.Questions, Has.Count.EqualTo(2));
            Assert.That(targetTheme.Questions[1].Right.Single(), Is.EqualTo("Test answer"));
        });
    }

    [Test]
    public async Task PasteQuestion_WithDifferentExistingMediaBytesFailsWithoutInsertingQuestion()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var sourceDocument = TestHelper.CreateSimpleTestPackage();
        using var targetDocument = TestHelper.CreateSimpleTestPackage();
        const string mediaName = "collision 例.png";
        await sourceDocument.Images.AddFileAsync(mediaName, new MemoryStream(new byte[] { 1, 2, 3 }));
        await targetDocument.Images.AddFileAsync(mediaName, new MemoryStream(new byte[] { 9, 8, 7 }));
        sourceDocument.Package.Rounds[0].Themes[0].Questions[0].Script!.Steps[0]
            .Parameters[StepParameterNames.Content].ContentValue!.Add(new ContentItem
            {
                Type = ContentTypes.Image,
                Value = mediaName,
                IsRef = true,
            });
        using var source = factory.CreateViewModelFor(sourceDocument, "collision source.siq");
        using var target = factory.CreateViewModelFor(targetDocument, "collision target.siq");
        source.ActiveNode = source.Package.Rounds[0].Themes[0].Questions[0];
        target.ActiveNode = target.Package.Rounds[0].Themes[0];
        Exception? pasteError = null;
        target.Error += (exception, _) => pasteError = exception;

        await ((IAsyncCommand)source.Copy).ExecuteAsync(null);
        await ((IAsyncCommand)target.Paste).ExecuteAsync(null);

        var streamInfo = target.Document.Images.GetFile(mediaName)!;
        await using var stream = streamInfo.Stream;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        Assert.Multiple(() =>
        {
            Assert.That(pasteError, Is.TypeOf<InvalidDataException>());
            Assert.That(target.Package.Rounds[0].Themes[0].Questions, Has.Count.EqualTo(1));
            Assert.That(buffer.ToArray(), Is.EqualTo(new byte[] { 9, 8, 7 }));
        });
    }

    [Test]
    public async Task CutQuestion_WhenClipboardWriteFails_DoesNotRemoveSource()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var sourceDocument = TestHelper.CreateSimpleTestPackage();
        using var source = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(sourceDocument, "source.siq");
        var sourceTheme = source.Package.Rounds[0].Themes[0];
        source.ActiveNode = sourceTheme.Questions[0];
        var clipboard = (ClipboardServiceMock)serviceProvider.GetRequiredService<IClipboardService>();
        clipboard.WriteException = new InvalidOperationException("Clipboard unavailable");
        Exception? reportedError = null;
        source.Error += (exception, _) => reportedError = exception;

        await ((IAsyncCommand)source.Cut).ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(sourceTheme.Questions, Has.Count.EqualTo(1));
            Assert.That(reportedError, Is.SameAs(clipboard.WriteException));
        });
    }

    [Test]
    public async Task CopyPastePackageInfo_AcrossDocuments_PreservesLegacyTextFallback()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var sourceDocument = SIDocument.Create("Источник", "Автор");
        using var targetDocument = SIDocument.Create("Target", "Other");
        using var source = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(sourceDocument, "source.siq");
        using var target = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(targetDocument, "target.siq");
        source.Package.Model.Info.Comments.Text = "Комментарий";
        source.Package.Model.Difficulty = 8;

        await ((IAsyncCommand)source.Package.CopyInfo).ExecuteAsync(null);
        var clipboard = serviceProvider.GetRequiredService<IClipboardService>();
        var legacyText = await clipboard.ReadTextAsync();
        var legacyInfo = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(legacyText!);
        await clipboard.WriteAsync(new ClipboardWriteRequest { Text = legacyText });
        await ((IAsyncCommand)target.Package.PasteInfo).ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(legacyInfo!["Comments"].GetString(), Is.EqualTo("Комментарий"));
            Assert.That(target.Package.Model.Info.Comments.Text, Is.EqualTo("Комментарий"));
            Assert.That(target.Package.Model.Difficulty, Is.EqualTo(8));
            Assert.That(target.Package.Model.Name, Is.EqualTo("Источник"));
        });
    }

    [Test]
    public async Task SpardTemplateCommands_UseTypedTextClipboard()
    {
        var clipboard = new ClipboardServiceMock();
        Exception? clipboardError = null;
        var viewModel = new SpardTemplateViewModel("Template", clipboard, exception => clipboardError = exception)
        {
            Transform = "A[lias]",
        };

        await ((IAsyncCommand)viewModel.Copy).ExecuteAsync(null);
        viewModel.Transform = "";
        await ((IAsyncCommand)viewModel.Paste).ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Transform, Is.EqualTo("A[lias]"));
            Assert.That(clipboardError, Is.Null);
        });
    }
}
