using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class MediaLibraryEditingTests
{
    private static readonly byte[] ImageBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZqxQAAAAASUVORK5CYII=");

    [Test]
    public async Task NeutralPicker_AddsStreamOnlyMediaAsOneUndoableOperation()
    {
        var firstStream = new MemoryStream(ImageBytes, writable: false);
        var secondBytes = ImageBytes.Concat(new byte[] { 1 }).ToArray();
        var secondStream = new MemoryStream(secondBytes, writable: false);
        var picker = new StubFilePicker([
            CreatePortalFile("первый 例.png", firstStream),
            CreatePortalFile("второй.png", secondStream),
        ]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Media library");

        await document.Images.AddFiles.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(firstStream.CanRead, Is.False);
            Assert.That(secondStream.CanRead, Is.False);
            Assert.That(document.Images.Files.Select(file => file.Name),
                Is.EqualTo(new[] { "первый 例.png", "второй.png" }));
            Assert.That(document.Images.CurrentFile?.Name, Is.EqualTo("второй.png"));
            Assert.That(picker.Requests.Single().AllowMultiple, Is.True);
            Assert.That(picker.Requests.Single().FileTypes.Single().Extensions, Does.Contain("png"));
            Assert.That(document.OperationsManager.Undo.CanBeExecuted, Is.True);
        });

        var selectedStream = document.Images.CurrentFile!.OpenStream()
            ?? throw new AssertionException("Selected image stream is unavailable.");
        await using (selectedStream.Stream)
        using (var copy = new MemoryStream())
        {
            await selectedStream.Stream.CopyToAsync(copy);
            Assert.That(copy.ToArray(), Is.EqualTo(secondBytes));
        }

        document.OperationsManager.Undo.Execute(null);
        Assert.That(document.Images.Files, Is.Empty);
        document.OperationsManager.Redo.Execute(null);
        Assert.That(document.Images.Files, Has.Count.EqualTo(2));

        document.Images.CurrentFile = document.Images.Files.Single(file => file.Name == "второй.png");
        document.Images.RemoveCurrentFile.Execute(null);
        Assert.That(document.Images.Files.Select(file => file.Name),
            Is.EqualTo(new[] { "первый 例.png" }));
        document.OperationsManager.Undo.Execute(null);
        Assert.That(document.Images.Files, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ExistingMedia_LinkGuardedRemoveAndSaveReloadPreserveReferenceAndBytes()
    {
        var picker = new StubFilePicker([
            CreatePortalFile("linked 例.png", new MemoryStream(ImageBytes, writable: false)),
        ]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Linked media");
        await document.Images.AddFiles.ExecuteAsync(null);
        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        var media = document.Images.Files.Single();
        document.ActiveNode = document.Package;
        document.Images.CurrentFile = media;

        Assert.That(document.Images.LinkCurrentToQuestion.CanBeExecuted, Is.False);

        document.ActiveNode = question;

        document.Images.LinkCurrentToQuestion.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(question.Model.GetContent(), Has.Some.Matches<ContentItem>(item =>
                item.Type == ContentTypes.Image
                && item.IsRef
                && item.Value == media.Name));
            Assert.That(document.Images.IsCurrentFileReferenced, Is.True);
            Assert.That(document.Images.RemoveCurrentFile.CanBeExecuted, Is.False);
        });

        document.Images.RemoveCurrentFile.Execute(null);
        Assert.That(document.Images.Files, Has.Count.EqualTo(1),
            "A referenced library file must not be deleted through the guarded command.");

        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester media library {Guid.NewGuid():N} 例.siq");
        document.Path = filePath;

        try
        {
            await document.Save.ExecuteAsync(null);
            await using var savedStream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(savedStream);
            var reloadedContent = reloaded.Package.Rounds[0].Themes[0].Questions[0].GetContent();
            var streamInfo = reloaded.Images.GetFile(media.Name)
                ?? throw new AssertionException("Linked image was not saved.");
            await using var imageStream = streamInfo.Stream;
            using var copy = new MemoryStream();
            await imageStream.CopyToAsync(copy);

            Assert.Multiple(() =>
            {
                Assert.That(reloadedContent, Has.Some.Matches<ContentItem>(item =>
                    item.Type == ContentTypes.Image && item.IsRef && item.Value == media.Name));
                Assert.That(copy.ToArray(), Is.EqualTo(ImageBytes));
            });
        }
        finally
        {
            document.Dispose();

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    public async Task OpenStreamAsync_WaitsForDocumentPersistenceWithoutBlockingAndSupportsCancellation()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = TestHelper.CreateSimpleTestPackage();
        await package.Images.AddFileAsync(
            "preview.png",
            new MemoryStream(ImageBytes, writable: false));
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Asynchronous media stream");
        var media = document.Images.Files.Single();
        var lockEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockHolder = Task.Run(async () => await document.Lock.WithLockAsync(async () =>
        {
            lockEntered.SetResult();
            await releaseLock.Task;
        }));

        await lockEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancellation = new CancellationTokenSource();
        var cancelledOpen = media.OpenStreamAsync(cancellation.Token).AsTask();
        Assert.That(cancelledOpen.IsCompleted, Is.False,
            "requesting a preview must return control while persistence owns the document lock");
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await cancelledOpen);

        var successfulOpen = media.OpenStreamAsync().AsTask();
        Assert.That(successfulOpen.IsCompleted, Is.False);
        releaseLock.SetResult();

        var streamInfo = await successfulOpen.WaitAsync(TimeSpan.FromSeconds(2))
            ?? throw new AssertionException("The image stream was unavailable after persistence completed.");
        await lockHolder.WaitAsync(TimeSpan.FromSeconds(2));
        await using var stream = streamInfo.Stream;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.That(copy.ToArray(), Is.EqualTo(ImageBytes));
    }

    [Test]
    public async Task ClosingDocument_CancelsPendingMediaPickerWithoutReportingError()
    {
        var picker = new BlockingFilePicker();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        using var package = TestHelper.CreateSimpleTestPackage();
        var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Closing media picker");
        Exception? reportedError = null;
        document.Error += (exception, _) => reportedError = exception;

        var addTask = document.Images.AddFiles.ExecuteAsync(null);
        await picker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        document.Dispose();
        await addTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(reportedError, Is.Null);
            Assert.That(document.Images.IsAddingFiles, Is.False);
            Assert.That(document.Images.Files, Is.Empty);
        });
    }

    [Test]
    public async Task AudioItem_CreatesFrameworkNeutralOwnedPlaybackSession()
    {
        var mediaBytes = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var previewService = new TrackingMediaPreviewService();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(
            mediaPreviewService: previewService);
        using var package = TestHelper.CreateSimpleTestPackage();
        await package.Audio.AddFileAsync(
            "звук 例.wav",
            new MemoryStream(mediaBytes, writable: false));
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Audio preview");
        var item = document.Audio.Files.Single();

        using (var session = item.CreatePreviewSession())
        {
            Assert.That(session.IsAvailable, Is.True);
            var captured = previewService.Source
                ?? throw new AssertionException("The media preview source was not captured.");
            using var stream = captured.OpenRead()
                ?? throw new AssertionException("The selected media stream was unavailable.");
            using var copy = new MemoryStream();
            await stream.Stream.CopyToAsync(copy);

            Assert.Multiple(() =>
            {
                Assert.That(item.IsPlayable, Is.True);
                Assert.That(captured.Kind, Is.EqualTo(MediaPreviewKind.Audio));
                Assert.That(captured.Name, Is.EqualTo("звук 例.wav"));
                Assert.That(copy.ToArray(), Is.EqualTo(mediaBytes));
                Assert.That(previewService.SessionDisposed, Is.False);
            });
        }

        Assert.That(previewService.SessionDisposed, Is.True);
    }

    private static PickedFile CreatePortalFile(string name, Stream stream) => new(
        localPath: null,
        displayName: name,
        extension: Path.GetExtension(name),
        _ => ValueTask.FromResult(stream));

    private sealed class StubFilePicker(IReadOnlyList<PickedFile> files) : IFilePickerService
    {
        internal List<OpenFilePickerRequest> Requests { get; } = [];

        public ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
            OpenFilePickerRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(files);
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

    private sealed class TrackingMediaPreviewService : IMediaPreviewService
    {
        public MediaPreviewSource? Source { get; private set; }

        public bool SessionDisposed { get; private set; }

        public IMediaPreviewSession CreateSession(MediaPreviewSource source)
        {
            Source = source;
            return new TrackingSession(() => SessionDisposed = true);
        }

        private sealed class TrackingSession(Action onDispose) : IMediaPreviewSession
        {
            public Uri? Source { get; } = new("http://127.0.0.1:5000/application/media-preview.html");

            public QuestionPreviewAvailability Availability => QuestionPreviewAvailability.Available;

            public QuestionPreviewBackendRequirement BackendRequirement => QuestionPreviewBackendRequirement.None;

            public void Dispose() => onDispose();
        }
    }
}
