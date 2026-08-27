using Microsoft.Extensions.DependencyInjection;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Services;
using SIQuester.ViewModel.Tests.Helpers;
using System.Text;
using Utils.Commands;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class ImportTextWorkspaceTests
{
    [Test]
    public void BufferedTextSource_ReleasesContentOnDispose()
    {
        var source = new BufferedTextSource("source.txt", "content"u8.ToArray());

        Assert.That(source.GetText(Encoding.UTF8), Is.EqualTo("content"));
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.GetText(Encoding.UTF8));
    }

    [Test]
    public async Task SelectFile_UsesNeutralStreamPickerAndPublishesExplicitUiState()
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetPreamble()
            .Concat("Пакет: портал 例"u8.ToArray())
            .ToArray();
        var source = new TrackingMemoryStream(bytes);
        OpenFilePickerRequest? request = null;
        var picker = new DelegateFilePickerService((pickerRequest, cancellationToken) =>
        {
            request = pickerRequest;
            return ValueTask.FromResult<IReadOnlyList<PickedFile>>(
                [new PickedFile(null, "вопросы 例.txt", ".txt", _ => ValueTask.FromResult<Stream>(source))]);
        });
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        var previousContext = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            using var workspace = new ImportTextViewModel(
                new AppOptions(),
                serviceProvider.GetRequiredService<IClipboardService>(),
                serviceProvider.GetRequiredService<IDocumentViewModelFactory>(),
                picker);

            Assert.Multiple(() =>
            {
                Assert.That(workspace.IsInitialState, Is.True);
                Assert.That(workspace.IsImportFileState, Is.False);
            });

            await ((IAsyncCommand)workspace.SelectFile).ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(request, Is.Not.Null);
                Assert.That(request!.AllowMultiple, Is.False);
                Assert.That(request.FileTypes.Single().Extensions, Is.EqualTo(new[] { "txt" }));
                Assert.That(workspace.IsInitialState, Is.False);
                Assert.That(workspace.IsImportFileState, Is.True);
                Assert.That(workspace.IsSplitState, Is.False);
                Assert.That(workspace.IsParseState, Is.False);
                Assert.That(workspace.FileName, Is.EqualTo("вопросы 例.txt"));
                Assert.That(workspace.ImportText, Is.EqualTo("Пакет: портал 例"));
                Assert.That(workspace.IsSelectingFile, Is.False);
                Assert.That(source.WasDisposed, Is.True);
            });

            workspace.ApproveImport.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(workspace.IsInitialState, Is.True);
                Assert.That(workspace.Text, Is.EqualTo("Пакет: портал 例"));
                Assert.That(workspace.FileName, Is.EqualTo("вопросы 例.txt"));
            });

            workspace.CancelImport.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(workspace.IsInitialState, Is.True);
                Assert.That(workspace.FileName, Is.Null);
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Test]
    public async Task Dispose_CancelsAnOutstandingNeutralPickerAndDisablesSelection()
    {
        var picker = new BlockingFilePickerService();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        var previousContext = SynchronizationContext.Current;
        ImportTextViewModel? workspace = null;

        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            workspace = new ImportTextViewModel(
                new AppOptions(),
                serviceProvider.GetRequiredService<IClipboardService>(),
                serviceProvider.GetRequiredService<IDocumentViewModelFactory>(),
                picker);
            var selection = ((IAsyncCommand)workspace.SelectFile).ExecuteAsync(null);
            await picker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            workspace.Dispose();
            await selection.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(picker.WasCancelled, Is.True);
                Assert.That(workspace.IsSelectingFile, Is.False);
                Assert.That(workspace.SelectFile.CanExecute(null), Is.False);
            });
        }
        finally
        {
            workspace?.Dispose();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Test]
    public async Task SelectFile_RejectsUnexpectedExtensionBeforeOpeningStream()
    {
        var streamOpened = false;
        var picker = new DelegateFilePickerService((_, _) =>
            ValueTask.FromResult<IReadOnlyList<PickedFile>>(
                [new PickedFile(null, "payload.exe", ".exe", _ =>
                {
                    streamOpened = true;
                    return ValueTask.FromResult<Stream>(new MemoryStream());
                })]));
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        var previousContext = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            using var workspace = new ImportTextViewModel(
                new AppOptions(),
                serviceProvider.GetRequiredService<IClipboardService>(),
                serviceProvider.GetRequiredService<IDocumentViewModelFactory>(),
                picker);
            Exception? reportedError = null;
            workspace.Error += (exception, _) => reportedError = exception;

            await ((IAsyncCommand)workspace.SelectFile).ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(streamOpened, Is.False);
                Assert.That(reportedError, Is.TypeOf<InvalidOperationException>());
                Assert.That(workspace.IsInitialState, Is.True);
                Assert.That(workspace.IsSelectingFile, Is.False);
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed class DelegateFilePickerService(
        Func<OpenFilePickerRequest, CancellationToken, ValueTask<IReadOnlyList<PickedFile>>> pickOpenFiles) :
        IFilePickerService
    {
        public ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
            OpenFilePickerRequest request,
            CancellationToken cancellationToken = default) => pickOpenFiles(request, cancellationToken);

        public ValueTask<PickedFile?> PickSaveFileAsync(
            SaveFilePickerRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<PickedFile?>(null);
    }

    private sealed class BlockingFilePickerService : IFilePickerService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCancelled { get; private set; }

        public async ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
            OpenFilePickerRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Array.Empty<PickedFile>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                WasCancelled = true;
                throw;
            }
        }

        public ValueTask<PickedFile?> PickSaveFileAsync(
            SaveFilePickerRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<PickedFile?>(null);
    }

    private sealed class TrackingMemoryStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
