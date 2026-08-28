using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class ContentMediaPickerEditingTests
{
    private static readonly byte[] ImageBytes = [1, 2, 3, 4];
    private static readonly byte[] SecondImageBytes = [5, 6, 7];
    private static readonly byte[] AudioBytes = [8, 9, 10];
    private static readonly byte[] VideoBytes = [11, 12, 13, 14];
    private static readonly byte[] HtmlBytes = "<p>Инертный HTML 例</p>"u8.ToArray();

    [Test]
    public async Task DirectPicker_AddsAllMediaTypesAtSelectionAsUndoableReferencesAndSaveReloadsBytes()
    {
        var picker = new SequenceFilePicker(
            [CreatePortalFile("рисунок 例.png", ImageBytes), CreatePortalFile("второй.png", SecondImageBytes)],
            [CreatePortalFile("звук 例.opus", AudioBytes)],
            [CreatePortalFile("видео 例.mp4", VideoBytes)],
            [CreatePortalFile("страница 例.html", HtmlBytes)]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Direct content media");
        var content = GetPrimaryContent(document);

        content.CurrentPosition = 0;
        await content.AddFile.ExecuteAsync(ContentTypes.Image);

        Assert.Multiple(() =>
        {
            Assert.That(content.Select(item => item.Model.Value),
                Is.EqualTo(new[] { "Test question text", "рисунок 例.png", "второй.png" }));
            Assert.That(document.Images.Files.Select(file => file.Name),
                Is.EqualTo(new[] { "рисунок 例.png", "второй.png" }));
            Assert.That(document.OperationsManager.Undo.CanBeExecuted, Is.True);
        });

        document.OperationsManager.Undo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(content, Has.Count.EqualTo(1));
            Assert.That(document.Images.Files, Is.Empty);
        });
        document.OperationsManager.Redo.Execute(null);

        foreach (var contentType in new[] { ContentTypes.Audio, ContentTypes.Video, ContentTypes.Html })
        {
            content.CurrentPosition = content.Count - 1;
            await content.AddFile.ExecuteAsync(contentType);
        }

        var expectedReferences = new[]
        {
            (ContentTypes.Image, "рисунок 例.png", ContentPlacements.Screen),
            (ContentTypes.Image, "второй.png", ContentPlacements.Screen),
            (ContentTypes.Audio, "звук 例.opus", ContentPlacements.Background),
            (ContentTypes.Video, "видео 例.mp4", ContentPlacements.Screen),
            (ContentTypes.Html, "страница 例.html", ContentPlacements.Screen),
        };

        Assert.Multiple(() =>
        {
            Assert.That(content.Skip(1).Select(item => (item.Model.Type, item.Model.Value, item.Model.Placement)),
                Is.EqualTo(expectedReferences));
            Assert.That(content.Skip(1).All(item => item.Model.IsRef), Is.True);
            Assert.That(picker.Requests, Has.Count.EqualTo(4));
            Assert.That(picker.Requests.All(request => request.AllowMultiple), Is.True);
            Assert.That(picker.Requests[0].FileTypes.Single().Extensions, Does.Contain("png"));
            Assert.That(picker.Requests[1].FileTypes.Single().Extensions, Does.Contain("opus"));
            Assert.That(picker.Requests[2].FileTypes.Single().Extensions, Does.Contain("mp4"));
            Assert.That(picker.Requests[3].FileTypes.Single().Extensions, Does.Contain("html"));
        });

        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester direct media {Guid.NewGuid():N} 例.siq");
        document.Path = filePath;

        try
        {
            await document.Save.ExecuteAsync(null);
            await using var savedStream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(savedStream);
            var reloadedContent = reloaded.Package.Rounds[0].Themes[0].Questions[0].GetContent().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(reloadedContent.Skip(1).Select(item => (item.Type, item.Value, item.Placement)),
                    Is.EqualTo(expectedReferences));
                Assert.That(ReadBytes(reloaded.Images, "рисунок 例.png"), Is.EqualTo(ImageBytes));
                Assert.That(ReadBytes(reloaded.Images, "второй.png"), Is.EqualTo(SecondImageBytes));
                Assert.That(ReadBytes(reloaded.Audio, "звук 例.opus"), Is.EqualTo(AudioBytes));
                Assert.That(ReadBytes(reloaded.Video, "видео 例.mp4"), Is.EqualTo(VideoBytes));
                Assert.That(ReadBytes(reloaded.Html, "страница 例.html"), Is.EqualTo(HtmlBytes));
            });
        }
        finally
        {
            document.Dispose();
            File.Delete(filePath);
        }
    }

    [Test]
    public async Task NonTopLevelContentPicker_RequestsOneFileAndReplacesExistingOptionContent()
    {
        var picker = new SequenceFilePicker(
            [CreatePortalFile("answer 例.png", ImageBytes), CreatePortalFile("ignored.png", SecondImageBytes)]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Answer content media");
        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        question.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Select);
        var optionContent = question.AnswerOptions!.GroupValue![0].Value.ContentValue!;
        optionContent.CurrentPosition = 0;

        await optionContent.AddFile.ExecuteAsync(ContentTypes.Image);

        Assert.Multiple(() =>
        {
            Assert.That(picker.Requests.Single().AllowMultiple, Is.False);
            Assert.That(optionContent, Has.Count.EqualTo(1));
            Assert.That(optionContent[0].Model.Type, Is.EqualTo(ContentTypes.Image));
            Assert.That(optionContent[0].Model.Value, Is.EqualTo("answer 例.png"));
            Assert.That(optionContent[0].Model.IsRef, Is.True);
            Assert.That(document.Images.Files.Select(file => file.Name), Is.EqualTo(new[] { "answer 例.png" }));
        });
    }

    [Test]
    public async Task DirectPicker_RejectsWrongMediaTypeBeforeOpeningOrMutating()
    {
        var streamOpened = false;
        var mismatchedFile = new PickedFile(
            localPath: null,
            displayName: "not-an-image.mp4",
            extension: ".mp4",
            _ =>
            {
                streamOpened = true;
                return ValueTask.FromResult<Stream>(new MemoryStream(VideoBytes, writable: false));
            });
        var picker = new SequenceFilePicker([mismatchedFile]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Mismatched direct media");
        var content = GetPrimaryContent(document);
        Exception? reportedError = null;
        document.Error += (exception, _) => reportedError = exception;

        await content.AddFile.ExecuteAsync(ContentTypes.Image);

        Assert.Multiple(() =>
        {
            Assert.That(streamOpened, Is.False);
            Assert.That(reportedError, Is.TypeOf<InvalidOperationException>());
            Assert.That(content, Has.Count.EqualTo(1));
            Assert.That(document.Images.Files, Is.Empty);
            Assert.That(document.Video.Files, Is.Empty);
            Assert.That(document.OperationsManager.Undo.CanBeExecuted, Is.False);
        });
    }

    [Test]
    public async Task ClosingDocument_CancelsDirectContentPickerWithoutMutationOrError()
    {
        var picker = new BlockingFilePicker();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        using var package = TestHelper.CreateSimpleTestPackage();
        var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Closing direct picker");
        var content = GetPrimaryContent(document);
        Exception? reportedError = null;
        document.Error += (exception, _) => reportedError = exception;

        var addTask = content.AddFile.ExecuteAsync(ContentTypes.Image);
        await picker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(content.IsAddingMediaFiles, Is.True);
        document.Dispose();
        await addTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(reportedError, Is.Null);
            Assert.That(content.IsAddingMediaFiles, Is.False);
            Assert.That(content, Has.Count.EqualTo(1));
            Assert.That(document.Images.Files, Is.Empty);
        });
    }

    private static ContentItemsViewModel GetPrimaryContent(QDocument document) =>
        document.Package.Rounds[0].Themes[0].Questions[0].ScriptSteps[0].Parameters
            .Single(parameter => parameter.Key == StepParameterNames.Content)
            .Value.ContentValue!;

    private static PickedFile CreatePortalFile(string name, byte[] bytes) => new(
        localPath: null,
        displayName: name,
        extension: Path.GetExtension(name),
        _ => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)));

    private static byte[] ReadBytes(DataCollection collection, string name)
    {
        var streamInfo = collection.GetFile(name)
            ?? throw new AssertionException($"Media '{name}' was not saved.");
        using var stream = streamInfo.Stream;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed class SequenceFilePicker(params IReadOnlyList<PickedFile>[] results) : IFilePickerService
    {
        private readonly Queue<IReadOnlyList<PickedFile>> _results = new(results);

        internal List<OpenFilePickerRequest> Requests { get; } = [];

        public ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
            OpenFilePickerRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(_results.Dequeue());
        }

        public ValueTask<PickedFile?> PickSaveFileAsync(
            SaveFilePickerRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingFilePicker : IFilePickerService
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
            OpenFilePickerRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Array.Empty<PickedFile>();
        }

        public ValueTask<PickedFile?> PickSaveFileAsync(
            SaveFilePickerRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
