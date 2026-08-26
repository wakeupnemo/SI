using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SIPackages;
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
        };

        var payload = SIQuesterClipboardSerializer.SerializeItem(item);
        var root = JsonNode.Parse(payload)!.AsObject();
        var succeeded = SIQuesterClipboardSerializer.TryDeserializeItem(payload, out var restored);

        Assert.Multiple(() =>
        {
            Assert.That(root["schemaVersion"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(root["kind"]!.GetValue<string>(), Is.EqualTo(SIQuesterClipboardSerializer.ItemKind));
            Assert.That(succeeded, Is.True);
            Assert.That(restored!.ItemLevel, Is.EqualTo(InfoOwnerData.Level.Question));
            Assert.That(restored.Images["лодка.png"], Is.EqualTo("/tmp/media with spaces/image.png"));
        });
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
