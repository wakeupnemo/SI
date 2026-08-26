using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using SIPackages;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Tests.Helpers;
using SIQuester.ViewModel.Tests.Mocks;
using System.Text.Json.Nodes;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class DocumentRecoveryTests
{
    private ServiceProvider _serviceProvider = null!;
    private TestAppPaths _appPaths = null!;
    private IDocumentViewModelFactory _documentFactory = null!;
    private IDocumentRecoveryService _recoveryService = null!;

    [SetUp]
    public void SetUp()
    {
        AppSettings.Default = new AppSettings { AutoSave = false };
        _serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        _appPaths = (TestAppPaths)_serviceProvider.GetRequiredService<IAppPaths>();
        _documentFactory = _serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        _recoveryService = _serviceProvider.GetRequiredService<IDocumentRecoveryService>();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        var root = Directory.GetParent(_appPaths.ConfigurationDirectory)!.FullName;

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Autosave_UnsavedDocument_WritesCompleteSemanticAndMediaSnapshot()
    {
        using var document = TestHelper.CreateSimpleTestPackage();
        using var viewModel = _documentFactory.CreateViewModelFor(document, "Новый пакет");
        var mediaDirectory = Path.Combine(_appPaths.CacheDirectory, "медиа 資料");
        Directory.CreateDirectory(mediaDirectory);
        var media = new[]
        {
            (Storage: viewModel.Images, Name: "картинка 例.png", Bytes: new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            (Storage: viewModel.Audio, Name: "звук 例.ogg", Bytes: new byte[] { 0x4F, 0x67, 0x67, 0x53 }),
            (Storage: viewModel.Video, Name: "видео 例.webm", Bytes: new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
            (Storage: viewModel.Html, Name: "страница 例.html", Bytes: "<p>Восстановление</p>"u8.ToArray()),
        };

        foreach (var item in media)
        {
            var sourcePath = Path.Combine(mediaDirectory, item.Name);
            await File.WriteAllBytesAsync(sourcePath, item.Bytes);
            item.Storage.AddFile(sourcePath);
        }

        viewModel.Package.Model.Name = "Несохраненный пакет";
        viewModel.Changed = true;
        await viewModel.SaveToTempAsync();

        var entry = (await _recoveryService.ListAsync()).Single();
        using var recovered = await _recoveryService.LoadAsync(entry);

        Assert.Multiple(() =>
        {
            Assert.That(entry.RecoveryId, Is.EqualTo(viewModel.RecoveryId));
            Assert.That(entry.OriginalPath, Is.Null);
            Assert.That(entry.DisplayName, Is.EqualTo("Новый пакет"));
            Assert.That(entry.SnapshotLength, Is.EqualTo(new FileInfo(entry.SnapshotPath).Length));
            Assert.That(recovered.Package.Name, Is.EqualTo("Несохраненный пакет"));
            Assert.That(recovered.Package.Rounds[0].Themes[0].Questions[0].Right.Single(), Is.EqualTo("Test answer"));
        });

        var recoveredCollections = new[] { recovered.Images, recovered.Audio, recovered.Video, recovered.Html };

        for (var index = 0; index < media.Length; index++)
        {
            var streamInfo = recoveredCollections[index].GetFile(media[index].Name);
            Assert.That(streamInfo, Is.Not.Null);
            await using var stream = streamInfo!.Stream;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            Assert.That(buffer.ToArray(), Is.EqualTo(media[index].Bytes));
        }
    }

    [Test]
    public async Task Autosave_TwoDocumentsWithSameFileName_UsesIndependentIdentities()
    {
        using var firstDocument = TestHelper.CreateSimpleTestPackage();
        using var secondDocument = TestHelper.CreateSimpleTestPackage();
        using var first = _documentFactory.CreateViewModelFor(firstDocument, "same.siq");
        using var second = _documentFactory.CreateViewModelFor(secondDocument, "same.siq");
        first.Path = Path.Combine(_appPaths.DataDirectory, "first", "same.siq");
        second.Path = Path.Combine(_appPaths.DataDirectory, "second", "same.siq");
        first.Package.Model.Name = "First";
        second.Package.Model.Name = "Second";
        first.Changed = true;
        second.Changed = true;

        await Task.WhenAll(
            first.SaveToTempAsync().AsTask(),
            second.SaveToTempAsync().AsTask());

        var entries = await _recoveryService.ListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries.Select(entry => entry.RecoveryId), Is.Unique);
            Assert.That(entries.Select(entry => entry.OriginalPath), Does.Contain(Path.GetFullPath(first.Path)));
            Assert.That(entries.Select(entry => entry.OriginalPath), Does.Contain(Path.GetFullPath(second.Path)));
        });
    }

    [Test]
    public async Task Autosave_ConcurrentWithManualSave_IsSerializedAndCanonicalSaveClearsRecovery()
    {
        using var document = TestHelper.CreateSimpleTestPackage();
        using var viewModel = _documentFactory.CreateViewModelFor(document, "serialized");
        var destinationPath = Path.Combine(_appPaths.DataDirectory, "serialized.siq");
        Directory.CreateDirectory(_appPaths.DataDirectory);
        viewModel.Path = destinationPath;
        viewModel.Package.Model.Name = "Serialized result";
        viewModel.Changed = true;

        await Task.WhenAll(
            viewModel.SaveToTempAsync().AsTask(),
            viewModel.SaveInternalAsync().AsTask());

        using var stream = File.OpenRead(destinationPath);
        using var reloaded = SIDocument.Load(stream);
        var entries = await _recoveryService.ListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Package.Name, Is.EqualTo("Serialized result"));
            Assert.That(viewModel.Changed, Is.False);
            Assert.That(entries, Is.Empty);
        });
    }

    [Test]
    public async Task Close_ConcurrentWithAutosave_WaitsAndLeavesNoRecoveryEntry()
    {
        using var document = TestHelper.CreateSimpleTestPackage();
        using var viewModel = _documentFactory.CreateViewModelFor(document, "closing");
        Directory.CreateDirectory(_appPaths.DataDirectory);
        viewModel.Path = Path.Combine(_appPaths.DataDirectory, "closing.siq");
        viewModel.Package.Model.Name = "Close-safe result";
        viewModel.Changed = true;

        await Task.WhenAll(
            viewModel.SaveToTempAsync().AsTask(),
            viewModel.Close.ExecuteAsync(null));

        var entries = await _recoveryService.ListAsync();
        using var stream = File.OpenRead(viewModel.Path);
        using var reloaded = SIDocument.Load(stream);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Is.Empty);
            Assert.That(reloaded.Package.Name, Is.EqualTo("Close-safe result"));
            Assert.That(viewModel.Changed, Is.False);
        });
    }

    [Test]
    public async Task Autosave_PreCancelled_LeavesNoPartialRecovery()
    {
        using var document = TestHelper.CreateSimpleTestPackage();
        using var viewModel = _documentFactory.CreateViewModelFor(document, "cancelled");
        viewModel.Changed = true;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await viewModel.SaveToTempAsync(cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(_appPaths.RecoveryDirectory)
                ? Directory.EnumerateFiles(_appPaths.RecoveryDirectory, "*", SearchOption.AllDirectories)
                : Array.Empty<string>(),
                Is.Empty);
            Assert.That(viewModel.Changed, Is.True);
        });
    }

    [Test]
    public async Task Autosave_SubsequentGeneration_ReplacesPointerAfterValidation()
    {
        using var document = TestHelper.CreateSimpleTestPackage();
        using var viewModel = _documentFactory.CreateViewModelFor(document, "generations");
        viewModel.Package.Model.Name = "First generation";
        viewModel.Changed = true;
        await viewModel.SaveToTempAsync();
        var firstEntry = (await _recoveryService.ListAsync()).Single();

        viewModel.Package.Model.Name = "Second generation";
        viewModel.Changed = true;
        await viewModel.SaveToTempAsync();
        var secondEntry = (await _recoveryService.ListAsync()).Single();
        using var recovered = await _recoveryService.LoadAsync(secondEntry);

        Assert.Multiple(() =>
        {
            Assert.That(secondEntry.SnapshotPath, Is.Not.EqualTo(firstEntry.SnapshotPath));
            Assert.That(File.Exists(firstEntry.SnapshotPath), Is.False);
            Assert.That(recovered.Package.Name, Is.EqualTo("Second generation"));
            Assert.That(Directory.EnumerateFiles(
                Path.GetDirectoryName(secondEntry.SnapshotPath)!,
                "document.*.siq"),
                Has.Exactly(1).Items);
        });
    }

    [Test]
    public async Task Inventory_PathTraversalMetadata_IsIgnoredAndRetainedForDiagnosis()
    {
        var recoveryId = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_appPaths.RecoveryDirectory, recoveryId);
        Directory.CreateDirectory(directory);
        var metadataPath = Path.Combine(directory, "recovery.json");
        var metadata = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["recoveryId"] = recoveryId,
            ["snapshotFileName"] = "../outside.siq",
            ["displayName"] = "Untrusted",
            ["savedAtUtc"] = DateTimeOffset.UtcNow,
            ["snapshotLength"] = 1,
            ["snapshotSha256"] = new string('0', 64),
        };
        await File.WriteAllTextAsync(metadataPath, metadata.ToJsonString());

        var entries = await _recoveryService.ListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entries, Is.Empty);
            Assert.That(File.Exists(metadataPath), Is.True);
        });
    }

    [Test]
    public async Task Inventory_NewerCorruptCanonical_DoesNotHideValidRecovery()
    {
        using var document = TestHelper.CreateSimpleTestPackage();
        using var viewModel = _documentFactory.CreateViewModelFor(document, "corrupt-canonical");
        var canonicalPath = Path.Combine(_appPaths.DataDirectory, "corrupt-canonical.siq");
        Directory.CreateDirectory(_appPaths.DataDirectory);
        viewModel.Path = canonicalPath;
        viewModel.Package.Model.Name = "Recover this";
        viewModel.Changed = true;
        await viewModel.SaveToTempAsync();
        await File.WriteAllTextAsync(canonicalPath, "not a package");
        File.SetLastWriteTimeUtc(canonicalPath, DateTime.UtcNow.AddMinutes(1));

        var entry = (await _recoveryService.ListAsync()).Single();

        Assert.That(entry.IsStale, Is.False);
    }

    [Test]
    public async Task Startup_ValidRecovery_IsOpenedDirtyWithOriginalIdentity()
    {
        var originalPath = Path.Combine(_appPaths.DataDirectory, "folder", "original.siq");
        string recoveryId;

        using (var document = TestHelper.CreateSimpleTestPackage())
        using (var source = _documentFactory.CreateViewModelFor(document, "Recovered display"))
        {
            source.Path = originalPath;
            source.Package.Model.Name = "Recovered content";
            source.Changed = true;
            await source.SaveToTempAsync();
            recoveryId = source.RecoveryId;
        }

        var previousContext = SynchronizationContext.Current;
        MainViewModel mainViewModel;

        try
        {
            SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
            mainViewModel = new MainViewModel(
                Array.Empty<string>(),
                new AppOptions(),
                _serviceProvider.GetRequiredService<IClipboardService>(),
                _serviceProvider,
                Substitute.For<IPlatformService>(),
                _documentFactory,
                _serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>(),
                _serviceProvider.GetRequiredService<IFilePickerService>(),
                _serviceProvider.GetRequiredService<IDialogService>(),
                _serviceProvider.GetRequiredService<IApplicationLifetimeService>(),
                _recoveryService);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        using (mainViewModel)
        {
            await mainViewModel.InitializeAsync();

            var recovered = mainViewModel.DocList.OfType<QDocument>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(recovered.Package.Model.Name, Is.EqualTo("Recovered content"));
                Assert.That(recovered.Path, Is.EqualTo(originalPath));
                Assert.That(recovered.FileName, Is.EqualTo("Recovered display"));
                Assert.That(recovered.RecoveryId, Is.EqualTo(recoveryId));
                Assert.That(recovered.Changed, Is.True);
            });
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }
}
