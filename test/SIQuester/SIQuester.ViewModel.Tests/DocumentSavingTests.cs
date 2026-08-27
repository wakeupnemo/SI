using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Services;
using SIQuester.ViewModel.Tests.Helpers;
using System.Security.Cryptography;
using System.Text.Json;

namespace SIQuester.ViewModel.Tests;

/// <summary>
/// Tests for document saving operations in QDocument ViewModel.
/// Tests simulate user behavior by creating, modifying, and saving documents.
/// </summary>
[TestFixture]
internal sealed class DocumentSavingTests
{
    private const string PreviewPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAIAAAACAAQMAAAD58POIAAAAA1BMVEX1xUI6Ko9WAAAAGUlEQVRIx2NgGAWjYBSMglEwCkbBKKAvAAAIgAABbisdVAAAAABJRU5ErkJggg==";

    private IServiceProvider _serviceProvider = null!;
    private IDocumentViewModelFactory _documentFactory = null!;
    private string _testDirectory = null!;

    [SetUp]
    public void Setup()
    {
        _serviceProvider = TestHelper.CreateServiceProvider();
        _documentFactory = _serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        _testDirectory = Path.Combine(Path.GetTempPath(), "SIQuester.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    #region Save Operations

    [Test]
    public async Task SaveDocument_ToNewFile_ShouldCreateFile()
    {
        // Arrange
        var document = TestHelper.CreateSimpleTestPackage();
        var qDocument = _documentFactory.CreateViewModelFor(document, "Test Package");
        var filePath = Path.Combine(_testDirectory, "test_package.siq");
        qDocument.Path = filePath;

        // Act
        await qDocument.Save.ExecuteAsync(null);

        // Assert
        Assert.That(File.Exists(filePath), Is.True, "Saved file should exist");
    }

    [Test]
    public async Task SaveDocument_WithChanges_ShouldPersistChanges()
    {
        // Arrange
        var document = TestHelper.CreateSimpleTestPackage();
        var qDocument = _documentFactory.CreateViewModelFor(document, "Test Package");
        var filePath = Path.Combine(_testDirectory, "test_package_modified.siq");
        qDocument.Path = filePath;

        // Modify the document
        qDocument.Package.Model.Name = "Modified Package Name";

        // Act - Save the document
        await qDocument.Save.ExecuteAsync(null);

        // Assert - File should exist
        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(File.Exists(filePath + ".bak"), Is.False, "Atomic-save backup should be removed after validation");

        // Load the document again to verify changes were saved
        using var stream = File.OpenRead(filePath);
        var loadedDocument = SIDocument.Load(stream);
        Assert.That(loadedDocument.Package.Name, Is.EqualTo("Modified Package Name"));
    }

    [Test]
    public async Task SaveDocument_AfterSave_ShouldClearChangedFlag()
    {
        // Arrange
        var document = TestHelper.CreateSimpleTestPackage();
        var qDocument = _documentFactory.CreateViewModelFor(document, "Test Package");
        var filePath = Path.Combine(_testDirectory, "test_clear_flag.siq");
        qDocument.Path = filePath;
        
        // Make a change
        qDocument.Package.Model.Name = "Modified Name";

        // Act
        await qDocument.Save.ExecuteAsync(null);

        // Assert
        Assert.That(qDocument.Changed, Is.False, "Changed flag should be cleared after save");
    }

    #endregion

    #region Save with Media

    [Test]
    public async Task SaveDocument_WithImages_ShouldIncludeMedia()
    {
        // Arrange
        var document = TestHelper.CreateSimpleTestPackage();
        var qDocument = _documentFactory.CreateViewModelFor(document, "Test Package");
        var filePath = Path.Combine(_testDirectory, "package_with_image.siq");
        qDocument.Path = filePath;

        // Add an image via the ViewModel (write to a temp file first, then add via ViewModel API)
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var tempImagePath = Path.Combine(_testDirectory, "test_image.png");
        await File.WriteAllBytesAsync(tempImagePath, imageData);
        qDocument.Images.AddFile(tempImagePath);

        // Act
        await qDocument.Save.ExecuteAsync(null);

        // Assert - Reload and check if image is present
        using var fileStream = File.OpenRead(filePath);
        var loadedDocument = SIDocument.Load(fileStream);
        Assert.That(loadedDocument.Images, Does.Contain("test_image.png"));
    }

    [Test]
    public async Task SaveDocument_WithAudio_ShouldIncludeMedia()
    {
        // Arrange
        var document = TestHelper.CreateSimpleTestPackage();
        var qDocument = _documentFactory.CreateViewModelFor(document, "Test Package");
        var filePath = Path.Combine(_testDirectory, "package_with_audio.siq");
        qDocument.Path = filePath;

        // Add audio via the ViewModel (write to a temp file first, then add via ViewModel API)
        var audioData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var tempAudioPath = Path.Combine(_testDirectory, "test_audio.mp3");
        await File.WriteAllBytesAsync(tempAudioPath, audioData);
        qDocument.Audio.AddFile(tempAudioPath);

        // Act
        await qDocument.Save.ExecuteAsync(null);

        // Assert - Reload and check if audio is present
        using var fileStream = File.OpenRead(filePath);
        var loadedDocument = SIDocument.Load(fileStream);
        Assert.That(loadedDocument.Audio, Does.Contain("test_audio.mp3"));
    }

    [Test]
    public async Task SaveDocument_AllMediaCollectionsAtUnicodePath_ShouldPreserveNamesAndBytes()
    {
        var unicodeDirectory = Path.Combine(_testDirectory, "Папка с пробелами", "資料");
        Directory.CreateDirectory(unicodeDirectory);

        var document = TestHelper.CreateSimpleTestPackage();
        var qDocument = _documentFactory.CreateViewModelFor(document, "Совместимость");
        var filePath = Path.Combine(unicodeDirectory, "Пакет викторины — 例.siq");
        qDocument.Path = filePath;

        var media = new[]
        {
            (Storage: qDocument.Images, Name: "картинка 例.png", Bytes: new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            (Storage: qDocument.Audio, Name: "звук 例.ogg", Bytes: new byte[] { 0x4F, 0x67, 0x67, 0x53 }),
            (Storage: qDocument.Video, Name: "видео 例.webm", Bytes: new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
            (Storage: qDocument.Html, Name: "страница 例.html", Bytes: "<p>Безопасный текст</p>"u8.ToArray()),
        };

        foreach (var item in media)
        {
            var sourcePath = Path.Combine(unicodeDirectory, item.Name);
            await File.WriteAllBytesAsync(sourcePath, item.Bytes);
            item.Storage.AddFile(sourcePath);
        }

        await qDocument.Save.ExecuteAsync(null);

        using var stream = File.OpenRead(filePath);
        using var loadedDocument = SIDocument.Load(stream);
        var loadedCollections = new[]
        {
            loadedDocument.Images,
            loadedDocument.Audio,
            loadedDocument.Video,
            loadedDocument.Html,
        };

        for (var index = 0; index < media.Length; index++)
        {
            var expected = media[index];
            var collection = loadedCollections[index];
            Assert.That(collection, Does.Contain(expected.Name));

            var streamInfo = collection.GetFile(expected.Name);
            Assert.That(streamInfo, Is.Not.Null);
            await using var mediaStream = streamInfo!.Stream;
            using var buffer = new MemoryStream();
            await mediaStream.CopyToAsync(buffer);
            Assert.That(buffer.ToArray(), Is.EqualTo(expected.Bytes));
        }
    }

    #endregion

    #region Save Validation

    [Test]
    public void QuestionTextEdit_ShouldSetChangedAndSupportUndo()
    {
        using var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = _documentFactory.CreateViewModelFor(document, "Question edit");
        var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];
        var originalText = question.QuestionText;

        question.QuestionText = "Edited through the typed inspector";

        Assert.That(qDocument.Changed, Is.True);
        Assert.That(qDocument.OperationsManager.Undo.CanBeExecuted, Is.True);
        qDocument.OperationsManager.Undo.Execute(null);
        Assert.That(question.QuestionText, Is.EqualTo(originalText));
    }

    [Test]
    public void QuestionTextEdit_EmptyScript_ShouldUndoAndRedoStructuralAddition()
    {
        using var document = TestHelper.CreateSimpleTestPackage();
        document.Package.Rounds[0].Themes[0].Questions[0].Script = new Script();
        using var qDocument = _documentFactory.CreateViewModelFor(document, "Question script edit");
        var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];

        question.QuestionText = "New script content";

        Assert.That(question.QuestionText, Is.EqualTo("New script content"));
        Assert.That(question.Model.Script!.Steps, Has.Count.EqualTo(1));
        qDocument.OperationsManager.Undo.Execute(null);
        Assert.That(question.QuestionText, Is.Empty);
        Assert.That(question.Model.Script.Steps, Is.Empty);
        qDocument.OperationsManager.Redo.Execute(null);
        Assert.That(question.QuestionText, Is.EqualTo("New script content"));
        Assert.That(question.Model.Script.Steps, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SaveDocument_WithComplexStructure_ShouldPreserveAllData()
    {
        // Arrange
        var document = SIDocument.Create("Complex Package", "Test Author");
        
        // Create multiple rounds, themes, and questions
        for (int r = 0; r < 2; r++)
        {
            var round = new Round { Name = $"Round {r + 1}" };
            for (int t = 0; t < 3; t++)
            {
                var theme = new Theme { Name = $"Theme {t + 1}" };
                for (int q = 0; q < 5; q++)
                {
                    var question = new Question { Price = (q + 1) * 100 };
                    question.Right.Add($"Answer {q + 1}");
                    theme.Questions.Add(question);
                }
                round.Themes.Add(theme);
            }
            document.Package.Rounds.Add(round);
        }

        var qDocument = _documentFactory.CreateViewModelFor(document, "Complex Package");
        var filePath = Path.Combine(_testDirectory, "complex_package.siq");
        qDocument.Path = filePath;

        // Act
        await qDocument.Save.ExecuteAsync(null);

        // Assert - Reload and verify structure
        using var fileStream = File.OpenRead(filePath);
        var loadedDocument = SIDocument.Load(fileStream);
        
        Assert.That(loadedDocument.Package.Rounds.Count, Is.EqualTo(2));
        Assert.That(loadedDocument.Package.Rounds[0].Themes.Count, Is.EqualTo(3));
        Assert.That(loadedDocument.Package.Rounds[0].Themes[0].Questions.Count, Is.EqualTo(5));
        Assert.That(loadedDocument.Package.Rounds[1].Themes[2].Questions[4].Price, Is.EqualTo(500));
    }

    [Test]
    public async Task CompatibilityArtifact_CreateEditSaveReload_ShouldPreserveSemanticDataAndMedia()
    {
        var artifactDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "compatibility-artifacts");
        Directory.CreateDirectory(artifactDirectory);
        var artifactPath = Path.Combine(artifactDirectory, "avalonia-core-roundtrip.siq");
        var receiptPath = Path.Combine(artifactDirectory, "avalonia-core-roundtrip.receipt.json");

        var document = TestHelper.CreateSimpleTestPackage();
        document.Package.ID = "avalonia-core-roundtrip";
        document.Package.Language = "ru-RU";
        document.Package.Publisher = "SI open-source compatibility test";
        document.Package.Tags.AddRange(["Avalonia", "совместимость"]);
        document.Package.Info.Comments.Text = "Комментарий пакета";
        document.Package.Info.ShowmanComments = new Comments { Text = "Комментарий ведущему" };
        document.Authors.Add(new AuthorInfo { Id = "author-1", Name = "Ада", Surname = "Лавлейс" });
        document.Sources.Add(new SourceInfo { Id = "source-1", Title = "Источник", Year = 2026 });
        document.Package.Info.Authors.Add("author-1");
        document.Package.Info.Sources.Add("source-1");

        var qDocument = _documentFactory.CreateViewModelFor(document, "Compatibility Artifact");
        qDocument.Path = artifactPath;
        var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];
        question.QuestionText = "Какой текст сохраняет новый редактор?";
        question.PrimaryRightAnswer = "Полный семантический текст";
        question.PrimaryWrongAnswer = "Потерянные данные";

        var mediaBytes = Convert.FromBase64String(PreviewPngBase64);
        var mediaSource = Path.Combine(_testDirectory, "медиа 例.png");
        await File.WriteAllBytesAsync(mediaSource, mediaBytes);
        qDocument.Images.AddFile(mediaSource);
        var previewContent = question.Model.Script!.Steps[0]
            .Parameters[StepParameterNames.Content].ContentValue!;
        previewContent[0].WaitForFinish = false;
        previewContent.Add(new ContentItem
        {
            Placement = ContentPlacements.Screen,
            Type = ContentTypes.Image,
            Value = "медиа 例.png",
            IsRef = true,
        });

        await qDocument.Save.ExecuteAsync(null);

        using var artifactStream = File.OpenRead(artifactPath);
        using var reloaded = SIDocument.Load(artifactStream);
        var reloadedQuestion = reloaded.Package.Rounds[0].Themes[0].Questions[0];
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Package.ID, Is.EqualTo("avalonia-core-roundtrip"));
            Assert.That(reloaded.Package.Language, Is.EqualTo("ru-RU"));
            Assert.That(reloaded.Package.Publisher, Is.EqualTo("SI open-source compatibility test"));
            Assert.That(reloaded.Package.Tags, Is.EqualTo(new[] { "Avalonia", "совместимость" }));
            Assert.That(reloaded.Package.Info.Comments.Text, Is.EqualTo("Комментарий пакета"));
            Assert.That(reloaded.Package.Info.ShowmanComments?.Text, Is.EqualTo("Комментарий ведущему"));
            Assert.That(reloaded.Authors.Single().Id, Is.EqualTo("author-1"));
            Assert.That(reloaded.Sources.Single().Id, Is.EqualTo("source-1"));
            Assert.That(reloadedQuestion.GetText(), Does.Contain("Какой текст сохраняет новый редактор?"));
            Assert.That(reloadedQuestion.Right[0], Is.EqualTo("Полный семантический текст"));
            Assert.That(reloadedQuestion.Wrong[0], Is.EqualTo("Потерянные данные"));
            Assert.That(reloaded.Images, Does.Contain("медиа 例.png"));
            Assert.That(
                reloadedQuestion.GetContent().Any(item =>
                    item.Type == ContentTypes.Image
                    && item.IsRef
                    && item.Value == "медиа 例.png"),
                Is.True);
        });

        var mediaInfo = reloaded.Images.GetFile("медиа 例.png");
        Assert.That(mediaInfo, Is.Not.Null);
        await using (var storedMedia = mediaInfo!.Stream)
        using (var buffer = new MemoryStream())
        {
            await storedMedia.CopyToAsync(buffer);
            Assert.That(buffer.ToArray(), Is.EqualTo(mediaBytes));
        }

        var artifactBytes = await File.ReadAllBytesAsync(artifactPath);
        var receipt = new
        {
            schema = 1,
            test = nameof(CompatibilityArtifact_CreateEditSaveReload_ShouldPreserveSemanticDataAndMedia),
            packageFile = Path.GetFileName(artifactPath),
            sha256 = Convert.ToHexString(SHA256.HashData(artifactBytes)).ToLowerInvariant(),
            size = artifactBytes.Length,
            packageId = reloaded.Package.ID,
            rounds = reloaded.Package.Rounds.Count,
            themes = reloaded.Package.Rounds.Sum(round => round.Themes.Count),
            questions = reloaded.Package.Rounds.Sum(round => round.Themes.Sum(theme => theme.Questions.Count)),
            media = new
            {
                images = reloaded.Images.Count,
                audio = reloaded.Audio.Count,
                video = reloaded.Video.Count,
                html = reloaded.Html.Count,
            },
            loader = "SIDocument.Load",
        };

        await File.WriteAllTextAsync(
            receiptPath,
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));

        TestContext.Out.WriteLine($"Compatibility artifact: {artifactPath}");
        TestContext.Out.WriteLine($"Compatibility receipt: {receiptPath}");
    }

    [Test]
    public async Task QuestionPreviewOptionsArtifact_CreateSaveReload_ShouldPreserveOptionMedia()
    {
        const string imageName = "вариант ответа 例.png";
        var artifactDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "compatibility-artifacts");
        Directory.CreateDirectory(artifactDirectory);
        var artifactPath = Path.Combine(artifactDirectory, "avalonia-preview-options.siq");
        var receiptPath = Path.Combine(artifactDirectory, "avalonia-preview-options.receipt.json");
        var mediaBytes = Convert.FromBase64String(PreviewPngBase64);
        var mediaSource = Path.Combine(_testDirectory, imageName);
        await File.WriteAllBytesAsync(mediaSource, mediaBytes);

        using var document = TestHelper.CreateSelectAnswerPreviewPackage(imageName);
        document.Package.ID = "avalonia-preview-options";
        document.Package.Language = "ru-RU";
        using var qDocument = _documentFactory.CreateViewModelFor(document, "Preview options artifact");
        qDocument.Path = artifactPath;
        qDocument.Images.AddFile(mediaSource);
        await qDocument.Save.ExecuteAsync(null);

        await using var artifactStream = File.OpenRead(artifactPath);
        using var reloaded = SIDocument.Load(artifactStream);
        var question = reloaded.Package.Rounds[0].Themes[0].Questions[0];
        var options = question.Parameters[QuestionParameterNames.AnswerOptions].GroupValue!;
        var imageOption = options["Б"].ContentValue!.Single();
        var mediaInfo = reloaded.Images.GetFile(imageName);
        Assert.That(mediaInfo, Is.Not.Null);
        await using var storedMedia = mediaInfo!.Stream;
        using var mediaBuffer = new MemoryStream();
        await storedMedia.CopyToAsync(mediaBuffer);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Package.ID, Is.EqualTo("avalonia-preview-options"));
            Assert.That(question.Parameters[QuestionParameterNames.AnswerType].SimpleValue,
                Is.EqualTo(StepParameterValues.SetAnswerTypeType_Select));
            Assert.That(options.Keys, Is.EqualTo(new[] { "А", "Б" }));
            Assert.That(question.Right, Is.EqualTo(new[] { "Б" }));
            Assert.That(imageOption.Type, Is.EqualTo(ContentTypes.Image));
            Assert.That(imageOption.Value, Is.EqualTo(imageName));
            Assert.That(imageOption.IsRef, Is.True);
            Assert.That(mediaBuffer.ToArray(), Is.EqualTo(mediaBytes));
        });

        var artifactBytes = await File.ReadAllBytesAsync(artifactPath);
        var receipt = new
        {
            schema = 1,
            test = nameof(QuestionPreviewOptionsArtifact_CreateSaveReload_ShouldPreserveOptionMedia),
            packageFile = Path.GetFileName(artifactPath),
            sha256 = Convert.ToHexString(SHA256.HashData(artifactBytes)).ToLowerInvariant(),
            size = artifactBytes.Length,
            packageId = reloaded.Package.ID,
            answerType = question.Parameters[QuestionParameterNames.AnswerType].SimpleValue,
            optionLabels = options.Keys,
            rightOption = question.Right.Single(),
            imageName,
            imageSha256 = Convert.ToHexString(SHA256.HashData(mediaBytes)).ToLowerInvariant(),
            loader = "SIDocument.Load",
        };

        await File.WriteAllTextAsync(
            receiptPath,
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));

        TestContext.Out.WriteLine($"Preview options artifact: {artifactPath}");
        TestContext.Out.WriteLine($"Preview options receipt: {receiptPath}");
    }

    #endregion

    #region Error Handling

    [Test]
    public async Task SaveDocument_CancelledBeforeWrite_ShouldLeaveExistingPackageUntouched()
    {
        var document = TestHelper.CreateSimpleTestPackage();
        var qDocument = _documentFactory.CreateViewModelFor(document, "Original Package");
        var filePath = Path.Combine(_testDirectory, "existing package.siq");
        qDocument.Path = filePath;
        await qDocument.Save.ExecuteAsync(null);
        var originalBytes = await File.ReadAllBytesAsync(filePath);

        qDocument.Package.Model.Name = "Replacement Must Not Commit";
        var persistence = _serviceProvider.GetRequiredService<IDocumentPersistenceService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await persistence.SaveAsync(qDocument, filePath, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(await File.ReadAllBytesAsync(filePath), Is.EqualTo(originalBytes));
        using var stream = File.OpenRead(filePath);
        using var reloaded = SIDocument.Load(stream);
        Assert.That(reloaded.Package.Name, Is.EqualTo("Test Package"));
        Assert.That(Directory.GetFiles(_testDirectory, ".existing package.siq.*.tmp"), Is.Empty);
    }

    [Test]
    public async Task SaveDocument_FinalValidationFailure_ShouldRestorePreviousPackage()
    {
        var document = TestHelper.CreateSimpleTestPackage();
        var qDocument = _documentFactory.CreateViewModelFor(document, "Original Package");
        var filePath = Path.Combine(_testDirectory, "rollback package.siq");
        qDocument.Path = filePath;
        await qDocument.Save.ExecuteAsync(null);
        var originalBytes = await File.ReadAllBytesAsync(filePath);

        qDocument.Package.Model.Name = "Replacement Must Roll Back";
        var validationCount = 0;
        var persistence = new SafeDocumentPersistenceService(
            _serviceProvider.GetRequiredService<ILoggerFactory>(),
            path =>
            {
                validationCount++;

                if (validationCount == 2)
                {
                    throw new InvalidDataException("Injected final validation failure");
                }

                using var validationStream = File.OpenRead(path);
                using var validationDocument = SIDocument.Load(validationStream);
            });

        Assert.That(
            async () => await persistence.SaveAsync(qDocument, filePath),
            Throws.InstanceOf<InvalidDataException>());

        Assert.That(await File.ReadAllBytesAsync(filePath), Is.EqualTo(originalBytes));
        using var stream = File.OpenRead(filePath);
        using var reloaded = SIDocument.Load(stream);
        Assert.That(reloaded.Package.Name, Is.EqualTo("Test Package"));
        Assert.That(Directory.GetFiles(_testDirectory, ".rollback package.siq.*.tmp"), Is.Empty);
        Assert.That(Directory.GetFiles(_testDirectory, "rollback package.siq.bak*"), Is.Empty);
    }

    [Test]
    [Platform("Linux")]
    public async Task SaveDocument_UnwritableDirectory_ShouldLeaveExistingPackageUntouched()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = _documentFactory.CreateViewModelFor(document, "Original Package");
        var protectedDirectory = Path.Combine(_testDirectory, "read only destination");
        Directory.CreateDirectory(protectedDirectory);
        var filePath = Path.Combine(protectedDirectory, "пакет 例.siq");
        qDocument.Path = filePath;
        await qDocument.Save.ExecuteAsync(null);
        var originalBytes = await File.ReadAllBytesAsync(filePath);
        qDocument.Package.Model.Name = "Replacement Must Not Commit";
        var persistence = _serviceProvider.GetRequiredService<IDocumentPersistenceService>();

        File.SetUnixFileMode(
            protectedDirectory,
            UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            Assert.That(
                async () => await persistence.SaveAsync(qDocument, filePath),
                Throws.InstanceOf<UnauthorizedAccessException>());
        }
        finally
        {
            File.SetUnixFileMode(
                protectedDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Assert.That(await File.ReadAllBytesAsync(filePath), Is.EqualTo(originalBytes));
        using var stream = File.OpenRead(filePath);
        using var reloaded = SIDocument.Load(stream);
        Assert.That(reloaded.Package.Name, Is.EqualTo("Test Package"));
        Assert.That(Directory.GetFiles(protectedDirectory, ".пакет 例.siq.*.tmp"), Is.Empty);
    }

    [Test]
    public void SaveDocument_WithInvalidPath_ShouldHandleGracefully()
    {
        // Arrange
        var document = TestHelper.CreateSimpleTestPackage();
        var qDocument = _documentFactory.CreateViewModelFor(document, "Test Package");
        
        // Set invalid path (empty)
        qDocument.Path = string.Empty;

        // Act & Assert - Should not throw, but may fail gracefully
        Assert.DoesNotThrowAsync(async () => await qDocument.Save.ExecuteAsync(null));
    }

    #endregion
}
