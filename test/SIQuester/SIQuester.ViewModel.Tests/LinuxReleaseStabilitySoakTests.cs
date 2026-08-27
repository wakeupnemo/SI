using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SIPackages;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Tests.Helpers;
using SIQuester.ViewModel.Tests.Mocks;
using System.Diagnostics;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
[NonParallelizable]
public sealed class LinuxReleaseStabilitySoakTests
{
    private const int WarmupCycles = 20;
    private const int MeasuredCycles = 50;

    [Test]
    [Explicit("Bounded v0.2.0 Linux release soak; run separately from ordinary regressions.")]
    [Category("Stability")]
    public async Task RepeatedOpenPreviewMediaEditAutosaveSaveReloadAndCloseSettles()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        var fixturePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "SIGameTestNew.siq");
        Assert.That(File.Exists(fixturePath), Is.True, fixturePath);
        var root = Path.Combine(Path.GetTempPath(), "SIQuesterStabilitySoak", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outputPath = Path.Combine(root, "повторное сохранение 例 с пробелами.siq");
        var previewService = new TrackingQuestionPreviewService();
        var mediaService = new TrackingMediaPreviewService();
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ConfirmSaveChangesAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(SaveChangesDecision.Save));
        var appPaths = new TestAppPaths(root);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(
            dialogService: dialogs,
            questionPreviewService: previewService,
            mediaPreviewService: mediaService,
            appPaths: appPaths);
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var latencies = new List<double>(MeasuredCycles);
        var samples = new List<ProcessSample>();

        try
        {
            for (var cycle = 0; cycle < WarmupCycles; cycle++)
            {
                await RunCycleAsync(mainViewModel, fixturePath, outputPath, cycle);
            }

            CollectGarbageAndSettle();
            samples.Add(ProcessSample.Capture(0));

            for (var cycle = 1; cycle <= MeasuredCycles; cycle++)
            {
                var stopwatch = Stopwatch.StartNew();
                await RunCycleAsync(mainViewModel, fixturePath, outputPath, WarmupCycles + cycle);
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);

                if (cycle % 10 == 0)
                {
                    CollectGarbageAndSettle();
                    samples.Add(ProcessSample.Capture(cycle));
                }
            }

            using (var outputStream = File.OpenRead(outputPath))
            using (var reloaded = SIDocument.Load(outputStream))
            {
                Assert.That(reloaded.Package.Name, Does.StartWith("Stability cycle"));
                Assert.That(reloaded.Images.Count, Is.GreaterThan(0));
                Assert.That(reloaded.Audio.Count, Is.GreaterThan(0));
                Assert.That(reloaded.Video.Count, Is.GreaterThan(0));
                Assert.That(reloaded.Html.Count, Is.GreaterThan(0));
            }

            var ordered = latencies.Order().ToArray();
            var baseline = samples[0];
            var final = samples[^1];
            TestContext.Progress.WriteLine(
                $"Linux stability soak: cycles={MeasuredCycles}; p50={Percentile(ordered, 0.50):F1}ms; "
                + $"p95={Percentile(ordered, 0.95):F1}ms; max={ordered[^1]:F1}ms");
            foreach (var sample in samples)
            {
                TestContext.Progress.WriteLine(
                    $"cycle={sample.Cycle}; rssKiB={sample.RssKiB}; threads={sample.Threads}; "
                    + $"fds={sample.FileDescriptors}; packageFds={sample.PackageFileDescriptors}");
            }

            Assert.Multiple(() =>
            {
                Assert.That(mainViewModel.DocList, Is.Empty);
                Assert.That(previewService.CreatedSessions, Is.EqualTo(WarmupCycles + MeasuredCycles));
                Assert.That(previewService.DisposedSessions, Is.EqualTo(previewService.CreatedSessions));
                Assert.That(mediaService.CreatedSessions, Is.EqualTo(2 * (WarmupCycles + MeasuredCycles)));
                Assert.That(mediaService.DisposedSessions, Is.EqualTo(mediaService.CreatedSessions));
                Assert.That(samples.All(sample => sample.PackageFileDescriptors == 0), Is.True,
                    "settled repeated cycles must not retain package/media file descriptors");
                Assert.That(final.FileDescriptors, Is.LessThanOrEqualTo(baseline.FileDescriptors + 4),
                    "the test host may initialize its four IPC descriptors once, but operation descriptors must settle");
                Assert.That(final.Threads, Is.LessThanOrEqualTo(baseline.Threads + 4),
                    "settled repeated cycles must not retain one thread per operation");
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RunCycleAsync(
        MainViewModel mainViewModel,
        string fixturePath,
        string outputPath,
        int cycle)
    {
        var document = await mainViewModel.OpenFileAsync(fixturePath)
            ?? throw new InvalidOperationException("The representative package did not open.");
        var questions = document.Package.Rounds
            .SelectMany(round => round.Themes)
            .SelectMany(theme => theme.Questions)
            .ToArray();
        var question = questions[cycle % questions.Length];
        document.Package.Model.Name = $"Stability cycle {cycle} 例";
        question.QuestionText = $"Repeated edit {cycle} — русский текст 例";
        document.ActiveNode = question;

        document.PlayQuestion.Execute(question);
        if (document.Dialog is WorkspaceViewModel preview)
        {
            await preview.Close.ExecuteAsync(null);
        }

        document.Images.Files[cycle % document.Images.Files.Count].OpenStream()?.Stream.Dispose();
        document.Html.Files[cycle % document.Html.Files.Count].OpenStream()?.Stream.Dispose();
        using (document.Audio.Files[cycle % document.Audio.Files.Count].CreatePreviewSession()) { }
        using (document.Video.Files[cycle % document.Video.Files.Count].CreatePreviewSession()) { }

        if (cycle % 5 == 0)
        {
            await mainViewModel.AutoSaveAsync();
        }

        document.Path = outputPath;
        await document.Save.ExecuteAsync(null);
        Assert.That(document.Changed, Is.False);
        Assert.That(await mainViewModel.TryCloseAsync(), Is.True);
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

    private static void CollectGarbageAndSettle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Thread.Sleep(250);
    }

    private static double Percentile(double[] values, double percentile) =>
        values[(int)Math.Clamp(Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1)];

    private sealed record ProcessSample(
        int Cycle,
        long RssKiB,
        int Threads,
        int FileDescriptors,
        int PackageFileDescriptors)
    {
        public static ProcessSample Capture(int cycle)
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var descriptorTargets = OperatingSystem.IsLinux() && Directory.Exists("/proc/self/fd")
                ? Directory.EnumerateFileSystemEntries("/proc/self/fd")
                    .Select(TryResolveLink)
                    .ToArray()
                : [];
            var packageFileDescriptors = descriptorTargets.Count(target => target != null
                && (target.EndsWith(".siq", StringComparison.OrdinalIgnoreCase)
                    || target.Contains("/Images/", StringComparison.Ordinal)
                    || target.Contains("/Audio/", StringComparison.Ordinal)
                    || target.Contains("/Video/", StringComparison.Ordinal)
                    || target.Contains("/Html/", StringComparison.Ordinal)));
            return new ProcessSample(
                cycle,
                process.WorkingSet64 / 1024,
                process.Threads.Count,
                descriptorTargets.Length,
                packageFileDescriptors);
        }

        private static string? TryResolveLink(string path)
        {
            try
            {
                return File.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private sealed class TrackingQuestionPreviewService : IQuestionPreviewService
    {
        public int CreatedSessions { get; private set; }
        public int DisposedSessions { get; private set; }

        public QuestionPreviewHostDescriptor GetHostDescriptor() =>
            QuestionPreviewHostDescriptor.Available(new Uri("http://127.0.0.1:52731/index.html"));

        public IQuestionPreviewSession CreateSession()
        {
            CreatedSessions++;
            return new TrackingQuestionSession(GetHostDescriptor(), () => DisposedSessions++);
        }
    }

    private sealed class TrackingQuestionSession(
        QuestionPreviewHostDescriptor host,
        Action onDispose) : IQuestionPreviewSession
    {
        private bool _disposed;
        public QuestionPreviewHostDescriptor Host { get; } = host;

        public bool TryGetMediaSource(QuestionPreviewMediaSource media, out string source)
        {
            source = $"http://127.0.0.1:52731/media/{Uri.EscapeDataString(media.Name)}";
            return true;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                onDispose();
            }
        }
    }

    private sealed class TrackingMediaPreviewService : IMediaPreviewService
    {
        public int CreatedSessions { get; private set; }
        public int DisposedSessions { get; private set; }

        public IMediaPreviewSession CreateSession(MediaPreviewSource source)
        {
            CreatedSessions++;
            return new TrackingMediaSession(() => DisposedSessions++);
        }
    }

    private sealed class TrackingMediaSession(Action onDispose) : IMediaPreviewSession
    {
        private bool _disposed;
        public Uri Source { get; } = new("http://127.0.0.1:52731/media-preview.html");
        public QuestionPreviewAvailability Availability => QuestionPreviewAvailability.Available;
        public QuestionPreviewBackendRequirement BackendRequirement => QuestionPreviewBackendRequirement.None;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                onDispose();
            }
        }
    }
}
