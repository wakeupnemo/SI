using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Tests.Helpers;
using SIQuester.ViewModel.Tests.Mocks;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
public sealed class MainLifecycleStabilityTests
{
    private SynchronizationContext? _previousSynchronizationContext;

    [SetUp]
    public void SetUp()
    {
        _previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
    }

    [TearDown]
    public void TearDown() => SynchronizationContext.SetSynchronizationContext(_previousSynchronizationContext);

    [Test]
    public async Task CorruptedOpenWithDefaultTokenRemovesLoaderAndRestoresSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "SIQuesterLifecycleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var packagePath = Path.Combine(root, "повреждённый пакет 例.siq");
        File.WriteAllText(packagePath, "not a zip package");
        var dialogs = Substitute.For<IDialogService>();
        dialogs.SelectOptionAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DialogOption>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>("close"));
        var loggerFactory = new RecordingLoggerFactory();
        var appPaths = new TestAppPaths(root);
        Directory.CreateDirectory(appPaths.RecoveryDirectory);
        Directory.CreateDirectory(appPaths.LogDirectory);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(
            dialogService: dialogs,
            loggerFactory: loggerFactory,
            appPaths: appPaths);
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var previousWorkspace = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(TestHelper.CreateSimpleTestPackage(), "previous");
        mainViewModel.DocList.Add(previousWorkspace);
        mainViewModel.ActiveDocument = previousWorkspace;

        try
        {
            var result = await mainViewModel.OpenFileAsync(packagePath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Null);
                Assert.That(mainViewModel.DocList.OfType<DocumentLoaderViewModel>(), Is.Empty);
                Assert.That(mainViewModel.DocList, Does.Contain(previousWorkspace));
                Assert.That(mainViewModel.ActiveDocument, Is.SameAs(previousWorkspace));
                Assert.That(loggerFactory.Entries.Any(entry =>
                    entry.Exception is InvalidDataException && entry.Level == LogLevel.Error), Is.True,
                    "the original package exception must reach persistent logging");
            });

            await dialogs.Received(1).SelectOptionAsync(
                Arg.Any<string>(),
                Arg.Is<IReadOnlyList<DialogOption>>(options =>
                    options.Any(option => option.Id == "open-autosave")
                    && options.Any(option => option.Id == "open-logs")),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task OverlappingCloseRequestsRunOneWorkspaceClose()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var workspace = new BlockingCloseWorkspace();
        mainViewModel.DocList.Add(workspace);

        var firstClose = mainViewModel.TryCloseAsync();
        await workspace.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var overlappingResult = await mainViewModel.TryCloseAsync();
        workspace.Release.TrySetResult();
        var firstResult = await firstClose;

        Assert.Multiple(() =>
        {
            Assert.That(firstResult, Is.True);
            Assert.That(overlappingResult, Is.False);
            Assert.That(workspace.CloseCalls, Is.EqualTo(1));
            Assert.That(mainViewModel.DocList, Is.Empty);
        });
    }

    [Test]
    public async Task WorkspaceCloseFailureIsLoggedReportedOnceAndKeepsWorkspace()
    {
        var dialogs = Substitute.For<IDialogService>();
        var loggerFactory = new RecordingLoggerFactory();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(
            dialogService: dialogs,
            loggerFactory: loggerFactory);
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var expected = new IOException("save destination is unavailable");
        var workspace = new ThrowingCloseWorkspace(expected);
        mainViewModel.DocList.Add(workspace);

        var result = await mainViewModel.TryCloseAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(mainViewModel.DocList, Does.Contain(workspace),
                "failed close must retain the workspace and its unsaved state");
            Assert.That(loggerFactory.Entries.Any(entry => ReferenceEquals(entry.Exception, expected)), Is.True);
        });
        await dialogs.Received(1).ShowErrorAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CloseCommandDelegatesToHostWithoutPreclosingDocuments()
    {
        var lifetime = Substitute.For<IApplicationLifetimeService>();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(
            applicationLifetimeService: lifetime);
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var workspace = new CountingCloseWorkspace();
        mainViewModel.DocList.Add(workspace);

        await mainViewModel.Close.ExecuteAsync(null);

        lifetime.Received(1).RequestExit();
        Assert.That(workspace.CloseCalls, Is.Zero,
            "the top-level window owns the single close/save transaction");
    }

    private static MainViewModel CreateMainViewModel(IServiceProvider serviceProvider) => new(
        Array.Empty<string>(),
        new AppOptions(),
        serviceProvider.GetRequiredService<IClipboardService>(),
        serviceProvider,
        Substitute.For<IPlatformService>(),
        serviceProvider.GetRequiredService<IDocumentViewModelFactory>(),
        serviceProvider.GetRequiredService<ILoggerFactory>(),
        serviceProvider.GetRequiredService<IFilePickerService>(),
        serviceProvider.GetRequiredService<IDialogService>(),
        serviceProvider.GetRequiredService<IUiDispatcher>(),
        serviceProvider.GetRequiredService<IApplicationLifetimeService>(),
        serviceProvider.GetRequiredService<IDocumentRecoveryService>(),
        serviceProvider.GetRequiredService<IPlatformCapabilities>(),
        serviceProvider.GetRequiredService<IExternalLauncher>());

    private sealed class BlockingCloseWorkspace : WorkspaceViewModel
    {
        public override string Header => "Blocking";
        public int CloseCalls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task Close_Executed(object? arg)
        {
            CloseCalls++;
            Started.TrySetResult();
            await Release.Task;
            OnClosed();
        }
    }

    private sealed class ThrowingCloseWorkspace(Exception exception) : WorkspaceViewModel
    {
        public override string Header => "Throwing";

        protected override Task Close_Executed(object? arg) => Task.FromException(exception);
    }

    private sealed class CountingCloseWorkspace : WorkspaceViewModel
    {
        public override string Header => "Counting";
        public int CloseCalls { get; private set; }

        protected override Task Close_Executed(object? arg)
        {
            CloseCalls++;
            OnClosed();
            return Task.CompletedTask;
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<LogEntry> Entries { get; } = [];

        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);

        public void Dispose() { }
    }

    private sealed class RecordingLogger(List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
    }
}
