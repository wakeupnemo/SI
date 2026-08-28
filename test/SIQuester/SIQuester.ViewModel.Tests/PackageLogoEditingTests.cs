using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class PackageLogoEditingTests
{
    private static readonly byte[] LogoBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZqxQAAAAASUVORK5CYII=");

    [Test]
    public async Task SelectLogo_StreamOnlyPickerTracksUndoAndPreservesMediaOnSaveReload()
    {
        var source = new MemoryStream(LogoBytes, writable: false);
        var pickedFile = new PickedFile(
            localPath: null,
            displayName: "логотип 例.png",
            extension: ".png",
            _ => ValueTask.FromResult<Stream>(source));
        var picker = new StubFilePicker([pickedFile]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var appPaths = serviceProvider.GetRequiredService<IAppPaths>();
        using var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Логотип 例");
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester logo {Guid.NewGuid():N} 例.siq");
        qDocument.Path = filePath;

        try
        {
            await qDocument.Package.SelectLogo.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(source.CanRead, Is.False, "The picker stream must be released after staging");
                Assert.That(qDocument.Package.Model.Logo, Is.EqualTo("@логотип 例.png"));
                Assert.That(qDocument.Package.HasLogo, Is.True);
                Assert.That(qDocument.Package.LogoName, Is.EqualTo("логотип 例.png"));
                Assert.That(qDocument.Images.Files.Select(image => image.Model.Name),
                    Does.Contain("логотип 例.png"));
                Assert.That(qDocument.Package.RemoveLogo.CanBeExecuted, Is.True);
                Assert.That(qDocument.OperationsManager.Undo.CanBeExecuted, Is.True);
                Assert.That(picker.Requests, Has.Count.EqualTo(1));
                Assert.That(picker.Requests[0].AllowMultiple, Is.False);
                Assert.That(picker.Requests[0].FileTypes.Single().Extensions, Does.Contain("png"));
                Assert.That(Directory.GetFiles(appPaths.TemporaryMediaDirectory), Has.Length.EqualTo(1));
            });

            qDocument.OperationsManager.Undo.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(qDocument.Package.HasLogo, Is.False);
                Assert.That(qDocument.Images.Files, Is.Empty,
                    "Logo and newly imported media must undo as one user operation");
            });

            qDocument.OperationsManager.Redo.Execute(null);
            Assert.That(qDocument.Package.Model.Logo, Is.EqualTo("@логотип 例.png"));

            await qDocument.Save.ExecuteAsync(null);

            Assert.That(Directory.GetFiles(appPaths.TemporaryMediaDirectory), Is.Empty,
                "A committed portal staging file must be deleted");
            await AssertSavedLogoAsync(filePath, expectedLogo: "@логотип 例.png", LogoBytes);

            qDocument.Package.RemoveLogo.Execute(null);
            Assert.That(qDocument.Package.HasLogo, Is.False);
            qDocument.OperationsManager.Undo.Execute(null);
            Assert.That(qDocument.Package.HasLogo, Is.True);
            qDocument.OperationsManager.Redo.Execute(null);
            Assert.That(qDocument.Package.HasLogo, Is.False);

            await qDocument.Save.ExecuteAsync(null);

            await using var reloadedStream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(reloadedStream);
            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Package.Logo, Is.Empty);
                Assert.That(reloaded.Images, Does.Contain("логотип 例.png"),
                    "Removing the logo reference must not silently delete reusable media");
            });
        }
        finally
        {
            qDocument.Dispose();

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    public async Task SelectLogo_CancelledPickerLeavesDocumentUnchanged()
    {
        var picker = new StubFilePicker([]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Cancelled logo");

        await qDocument.Package.SelectLogo.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(qDocument.Package.HasLogo, Is.False);
            Assert.That(qDocument.Images.Files, Is.Empty);
            Assert.That(qDocument.Package.SelectLogo.CanBeExecuted, Is.True);
            Assert.That(qDocument.Package.RemoveLogo.CanBeExecuted, Is.False);
            Assert.That(qDocument.OperationsManager.Undo.CanBeExecuted, Is.False);
        });
    }

    [Test]
    public async Task SelectLogo_PickerCancellationIsNotReportedAsAnError()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(new CancelledFilePicker());
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Cancelled logo picker");
        Exception? reportedError = null;
        qDocument.Error += (exception, _) => reportedError = exception;

        await qDocument.Package.SelectLogo.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(reportedError, Is.Null);
            Assert.That(qDocument.Package.HasLogo, Is.False);
            Assert.That(qDocument.Images.Files, Is.Empty);
            Assert.That(qDocument.Package.SelectLogo.CanBeExecuted, Is.True);
            Assert.That(qDocument.OperationsManager.Undo.CanBeExecuted, Is.False);
        });
    }

    [Test]
    public async Task SelectLogo_UnsavedPortalMediaIsDeletedWhenDocumentCloses()
    {
        var source = new MemoryStream(LogoBytes, writable: false);
        var picker = new StubFilePicker([
            new PickedFile(
                localPath: null,
                displayName: "unsaved logo.png",
                extension: ".png",
                _ => ValueTask.FromResult<Stream>(source)),
        ]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var appPaths = serviceProvider.GetRequiredService<IAppPaths>();
        using var document = TestHelper.CreateSimpleTestPackage();
        var qDocument = factory.CreateViewModelFor(document, "Unsaved logo");

        await qDocument.Package.SelectLogo.ExecuteAsync(null);
        var stagedPath = Directory.GetFiles(appPaths.TemporaryMediaDirectory).Single();

        qDocument.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(source.CanRead, Is.False);
            Assert.That(File.Exists(stagedPath), Is.False,
                "Closing an unsaved document must release and delete portal staging media");
        });
    }

    [Test]
    public async Task SelectLogo_RejectedQualityFileDeletesPortalStagingAndReportsError()
    {
        var source = new MemoryStream(LogoBytes, writable: false);
        var picker = new StubFilePicker([
            new PickedFile(
                localPath: null,
                displayName: "rejected logo.bmp",
                extension: ".bmp",
                _ => ValueTask.FromResult<Stream>(source)),
        ]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var appPaths = serviceProvider.GetRequiredService<IAppPaths>();
        using var document = TestHelper.CreateSimpleTestPackage();
        document.Package.HasQualityControl = true;
        using var qDocument = factory.CreateViewModelFor(document, "Rejected logo");
        Exception? reportedError = null;
        qDocument.Error += (exception, _) => reportedError = exception;

        await qDocument.Package.SelectLogo.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(source.CanRead, Is.False);
            Assert.That(reportedError, Is.TypeOf<InvalidOperationException>());
            Assert.That(qDocument.Package.HasLogo, Is.False);
            Assert.That(qDocument.Images.Files, Is.Empty);
            Assert.That(Directory.GetFiles(appPaths.TemporaryMediaDirectory), Is.Empty,
                "A rejected portal file must not be retained in the staging directory");
            Assert.That(qDocument.Package.SelectLogo.CanBeExecuted, Is.True);
            Assert.That(qDocument.OperationsManager.Undo.CanBeExecuted, Is.False);
        });
    }

    [Test]
    public async Task PortalMediaApplyCancellationKeepsStagingAvailableForRetry()
    {
        var source = new MemoryStream(LogoBytes, writable: false);
        var picker = new StubFilePicker([
            new PickedFile(
                localPath: null,
                displayName: "retry logo.png",
                extension: ".png",
                _ => ValueTask.FromResult<Stream>(source)),
        ]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var appPaths = serviceProvider.GetRequiredService<IAppPaths>();
        using var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Retry logo");
        using var cancelledTarget = SIDocument.Create("Cancelled target", "Test author");

        await qDocument.Package.SelectLogo.ExecuteAsync(null);
        var stagedPath = Directory.GetFiles(appPaths.TemporaryMediaDirectory).Single();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await qDocument.Images.ApplyToAsync(
                cancelledTarget.Images,
                cancellationToken: cancellation.Token));
        Assert.That(File.Exists(stagedPath), Is.True,
            "A failed apply must retain portal staging media for a later save retry");

        using var retryTarget = SIDocument.Create("Retry target", "Test author");
        await qDocument.Images.ApplyToAsync(retryTarget.Images);
        var streamInfo = retryTarget.Images.GetFile("retry logo.png")
            ?? throw new AssertionException("Retried logo media is missing.");
        await using var retriedStream = streamInfo.Stream;
        using var content = new MemoryStream();
        await retriedStream.CopyToAsync(content);

        Assert.Multiple(() =>
        {
            Assert.That(content.ToArray(), Is.EqualTo(LogoBytes));
            Assert.That(File.Exists(stagedPath), Is.True,
                "A non-final apply must not consume the document's pending source");
        });
    }

    private static async Task AssertSavedLogoAsync(string path, string expectedLogo, byte[] expectedBytes)
    {
        await using var stream = File.OpenRead(path);
        using var reloaded = SIDocument.Load(stream);
        var streamInfo = reloaded.Images.GetFile("логотип 例.png")
            ?? throw new AssertionException("Reloaded logo media is missing.");
        await using var imageStream = streamInfo.Stream;
        using var content = new MemoryStream();
        await imageStream.CopyToAsync(content);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Package.Logo, Is.EqualTo(expectedLogo));
            Assert.That(content.ToArray(), Is.EqualTo(expectedBytes));
        });
    }

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

    private sealed class CancelledFilePicker : IFilePickerService
    {
        public ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
            OpenFilePickerRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IReadOnlyList<PickedFile>>(new OperationCanceledException());

        public ValueTask<PickedFile?> PickSaveFileAsync(
            SaveFilePickerRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
