using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Services;
using SIQuester.ViewModel.Tests.Helpers;
using System.Text;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class ExternalFileImportTests
{
    [TestCase("package.SIQ", "", ExternalDropFileKind.Package, null, null)]
    [TestCase("questions", "txt", ExternalDropFileKind.Text, null, null)]
    [TestCase("изображение 例.JPEG", ".JPEG", ExternalDropFileKind.Image, CollectionNames.ImagesStorageName, ContentTypes.Image)]
    [TestCase("sound.OPUS", "", ExternalDropFileKind.Audio, CollectionNames.AudioStorageName, ContentTypes.Audio)]
    [TestCase("movie.mp4", "", ExternalDropFileKind.Video, CollectionNames.VideoStorageName, ContentTypes.Video)]
    [TestCase("safe-preview.html", "", ExternalDropFileKind.Html, CollectionNames.HtmlStorageName, ContentTypes.Html)]
    [TestCase("program.exe", "", ExternalDropFileKind.Unsupported, null, null)]
    [TestCase("program.exe", ".png", ExternalDropFileKind.Unsupported, null, null)]
    public void Classifier_RecognizesOnlySupportedFileRoles(
        string displayName,
        string extension,
        ExternalDropFileKind expectedKind,
        string? expectedCollection,
        string? expectedContentType)
    {
        var result = ExternalDropClassifier.Classify(displayName, extension);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(expectedKind));
            Assert.That(result.CollectionName, Is.EqualTo(expectedCollection));
            Assert.That(result.ContentType, Is.EqualTo(expectedContentType));
            Assert.That(result.IsSupported, Is.EqualTo(expectedKind != ExternalDropFileKind.Unsupported));
        });
    }

    [Test]
    public async Task StreamOnlyMediaFiles_ImportIntoExistingScriptContentAndSurviveSaveReload()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = factory.CreateViewModelFor(package, "External media");
        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        var media = new[]
        {
            ("картинка 例.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, ContentTypes.Image),
            ("звук 例.mp3", new byte[] { 1, 2, 3, 4 }, ContentTypes.Audio),
            ("видео 例.mp4", new byte[] { 5, 6, 7, 8 }, ContentTypes.Video),
            ("страница 例.html", "<p>not executed</p>"u8.ToArray(), ContentTypes.Html),
        };
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester external media {Guid.NewGuid():N} 例.siq");
        document.Path = filePath;

        try
        {
            foreach (var item in media)
            {
                var result = await document.ImportExternalFileAsync(
                    CreateStreamOnlyFile(item.Item1, item.Item2),
                    question);
                Assert.That(result, Is.EqualTo(ExternalFileImportResult.Imported));
            }

            await document.Save.ExecuteAsync(null);

            await using var packageStream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(packageStream);
            var reloadedContent = reloaded.Package.Rounds[0].Themes[0].Questions[0]
                .Script!.Steps[0].Parameters[StepParameterNames.Content].ContentValue!;

            foreach (var item in media)
            {
                var contentItem = reloadedContent.Single(content => content.Value == item.Item1);
                var collection = item.Item3 switch
                {
                    ContentTypes.Image => reloaded.Images,
                    ContentTypes.Audio => reloaded.Audio,
                    ContentTypes.Video => reloaded.Video,
                    ContentTypes.Html => reloaded.Html,
                    _ => throw new InvalidOperationException($"Unexpected media type {item.Item3}"),
                };
                var streamInfo = collection.GetFile(item.Item1);
                Assert.That(streamInfo, Is.Not.Null);
                await using var importedStream = streamInfo!.Stream;
                using var importedBytes = new MemoryStream();
                await importedStream.CopyToAsync(importedBytes);

                Assert.Multiple(() =>
                {
                    Assert.That(contentItem.Type, Is.EqualTo(item.Item3));
                    Assert.That(contentItem.IsRef, Is.True);
                    Assert.That(importedBytes.ToArray(), Is.EqualTo(item.Item2));
                });
            }
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public async Task MediaImport_CreatesLegacyContentAsOneUndoableChange()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var package = CreateLegacyPackageWithoutContent();
        using var document = factory.CreateViewModelFor(package, "Legacy external media");
        var question = document.Package.Rounds[0].Themes[0].Questions[0];

        var result = await document.ImportExternalFileAsync(
            CreateStreamOnlyFile("photo.png", new byte[] { 1, 3, 3, 7 }),
            question);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(ExternalFileImportResult.Imported));
            Assert.That(question.HasScript, Is.False);
            Assert.That(question.LegacyContent, Has.Count.EqualTo(1));
            Assert.That(document.Images.Files, Has.Count.EqualTo(1));
        });

        document.OperationsManager.Undo.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(question.Model.Parameters.ContainsKey(QuestionParameterNames.Question), Is.False);
            Assert.That(question.LegacyContent, Is.Null);
            Assert.That(document.Images.Files, Is.Empty);
        });

        document.OperationsManager.Redo.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(question.LegacyContent, Has.Count.EqualTo(1));
            Assert.That(question.LegacyContent![0].Model.Value, Is.EqualTo("photo.png"));
            Assert.That(document.Images.Files, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task PackageAndTextFiles_AreSeparatedFromMediaAndDelegatedToHost()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = factory.CreateViewModelFor(package, "External host files");
        var requests = new List<ExternalDropFileKind>();
        document.ExternalFileImportRequested += (_, kind, _) =>
        {
            requests.Add(kind);
            return Task.FromResult(true);
        };

        var packageResult = await document.ImportExternalFileAsync(
            CreateStreamOnlyFile("package.siq", new byte[] { 1 }),
            targetQuestion: null);
        var textResult = await document.ImportExternalFileAsync(
            CreateStreamOnlyFile("questions.txt", new byte[] { 2 }),
            targetQuestion: null);
        var mediaWithoutQuestion = await document.ImportExternalFileAsync(
            CreateStreamOnlyFile("image.png", new byte[] { 3 }),
            targetQuestion: null);

        Assert.Multiple(() =>
        {
            Assert.That(packageResult, Is.EqualTo(ExternalFileImportResult.Imported));
            Assert.That(textResult, Is.EqualTo(ExternalFileImportResult.Imported));
            Assert.That(mediaWithoutQuestion, Is.EqualTo(ExternalFileImportResult.QuestionTargetRequired));
            Assert.That(requests, Is.EqualTo(new[] { ExternalDropFileKind.Package, ExternalDropFileKind.Text }));
            Assert.That(document.Images.Files, Is.Empty);
        });
    }

    [Test]
    public async Task MainHost_OpensStreamOnlyPackageAndCreatesRepeatableTextImportWorkspace()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var previousContext = SynchronizationContext.Current;
        MainViewModel? main = null;

        try
        {
            SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
            main = new MainViewModel(
                Array.Empty<string>(),
                new AppOptions(),
                serviceProvider.GetRequiredService<IClipboardService>(),
                serviceProvider,
                Substitute.For<IPlatformService>(),
                factory,
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>(),
                serviceProvider.GetRequiredService<IFilePickerService>(),
                serviceProvider.GetRequiredService<IDialogService>(),
                serviceProvider.GetRequiredService<IUiDispatcher>(),
                serviceProvider.GetRequiredService<IApplicationLifetimeService>(),
                serviceProvider.GetRequiredService<IDocumentRecoveryService>(),
                serviceProvider.GetRequiredService<IPlatformCapabilities>(),
                serviceProvider.GetRequiredService<IExternalLauncher>());

            var editorPackage = TestHelper.CreateSimpleTestPackage();
            var editor = factory.CreateViewModelFor(editorPackage, "Drop target");
            main.DocList.Add(editor);

            var packageResult = await editor.ImportExternalFileAsync(
                CreateStreamOnlyFile("Открытый пакет 例.siq", CreatePackageBytes("Dropped package")),
                targetQuestion: null);
            var textBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                .GetPreamble()
                .Concat("Пакет: текстовый импорт 例"u8.ToArray())
                .ToArray();
            var textResult = await editor.ImportExternalFileAsync(
                CreateStreamOnlyFile("вопросы 例.txt", textBytes),
                targetQuestion: null);

            var openedPackage = main.DocList.OfType<QDocument>()
                .Single(document => document.Package.Model.Name == "Dropped package");
            var textImport = main.DocList.OfType<ImportTextViewModel>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(packageResult, Is.EqualTo(ExternalFileImportResult.Imported));
                Assert.That(openedPackage.Path, Is.Empty);
                Assert.That(openedPackage.FileName, Is.EqualTo("Открытый пакет 例"));
                Assert.That(textResult, Is.EqualTo(ExternalFileImportResult.Imported));
                Assert.That(textImport.FileName, Is.EqualTo("вопросы 例.txt"));
                Assert.That(textImport.ImportText, Is.EqualTo("Пакет: текстовый импорт 例"));
            });

            textImport.TextEncoding = Encoding.GetEncoding(1251);
            textImport.TextEncoding = Encoding.UTF8;
            Assert.That(textImport.ImportText, Is.EqualTo("Пакет: текстовый импорт 例"));

            var malformedResult = await editor.ImportExternalFileAsync(
                CreateStreamOnlyFile("broken.siq", "not a package"u8.ToArray()),
                targetQuestion: null);
            Assert.Multiple(() =>
            {
                Assert.That(malformedResult, Is.EqualTo(ExternalFileImportResult.Failed));
                Assert.That(main.DocList.OfType<DocumentLoaderViewModel>(), Is.Empty,
                    "a rejected external package must not leave a dead loading workspace");
            });
        }
        finally
        {
            main?.Dispose();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Test]
    public void PreCancelledMediaImport_DoesNotCreateContentOrTemporaryMedia()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = factory.CreateViewModelFor(package, "Cancelled external media");
        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        var initialContentCount = question.ScriptSteps[0].Parameters
            .Single().Value.ContentValue!.Count;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await document.ImportExternalFileAsync(
                CreateStreamOnlyFile("cancelled.png", new byte[] { 1, 2, 3 }),
                question,
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(question.ScriptSteps[0].Parameters.Single().Value.ContentValue,
                Has.Count.EqualTo(initialContentCount));
            Assert.That(document.Images.Files, Is.Empty);
        });
    }

    private static PickedFile CreateStreamOnlyFile(string name, byte[] content) => new(
        null,
        name,
        Path.GetExtension(name),
        cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new MemoryStream(content, writable: false));
        });

    private static SIDocument CreateLegacyPackageWithoutContent()
    {
        var document = SIDocument.Create("Legacy", "Author");
        var question = new Question { Price = 100 };
        question.Right.Add("Answer");
        var theme = new Theme { Name = "Theme", Questions = { question } };
        document.Package.Rounds.Add(new Round { Name = "Round", Themes = { theme } });
        return document;
    }

    private static byte[] CreatePackageBytes(string packageName)
    {
        using var stream = new MemoryStream();

        using (var document = SIDocument.Create(packageName, "Author", stream, true))
        {
            document.Save();
        }

        return stream.ToArray();
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            var previousContext = Current;

            try
            {
                SetSynchronizationContext(this);
                callback(state);
            }
            finally
            {
                SetSynchronizationContext(previousContext);
            }
        }
    }
}
