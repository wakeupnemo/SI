using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Properties;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class StatisticsEditingTests
{
    [Test]
    public async Task Refresh_ReportsTypedCountsAndNavigatesTextIssueToCanonicalQuestion()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = CreateInvalidPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Validation");
        var statistics = document.Statistics;
        statistics.CheckEmptyAuthors = true;
        statistics.CheckEmptySources = true;

        await statistics.Create.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(statistics.RoundCount, Is.EqualTo(1));
            Assert.That(statistics.ThemeCount, Is.EqualTo(1));
            Assert.That(statistics.QuestionCount, Is.EqualTo(1));
            Assert.That(statistics.MediaFileCount, Is.Zero);
            Assert.That(statistics.WarningCount, Is.EqualTo(4));
            Assert.That(statistics.Result, Does.Contain(Resources.NoQuestion));
            Assert.That(statistics.Result, Does.Contain(Resources.NoAnswer));
            Assert.That(statistics.Result, Does.Contain(Resources.NoSource));
            Assert.That(statistics.HasWarnings, Is.True);
        });

        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        var warning = statistics.Warnings.Single(item =>
            item.Title.Contains(Resources.NoAnswer, StringComparison.CurrentCulture));
        warning.NavigateToSource.Execute(null);
        await WaitUntilAsync(() => ReferenceEquals(document.ActiveNode, question));

        Assert.That(document.ActiveNode, Is.SameAs(question));
    }

    [Test]
    public async Task Refresh_UnusedMediaWarningNavigatesToCorrectTypedStorage()
    {
        var sourcePath = CreateTemporaryMediaFile("mp3", [1, 2, 3]);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Media validation");

        try
        {
            document.Audio.AddFile(sourcePath, "звук 例.mp3");
            var statistics = document.Statistics;
            await statistics.Create.ExecuteAsync(null);
            var warning = statistics.Warnings.Single(item =>
                item.Title.Contains("звук 例.mp3", StringComparison.Ordinal));
            warning.NavigateToSource.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(document.SideIndex, Is.EqualTo(3));
                Assert.That(document.Audio.CurrentFile?.Name, Is.EqualTo("звук 例.mp3"));
                Assert.That(document.Images.CurrentFile, Is.Null);
            });
        }
        finally
        {
            document.Dispose();
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task RemoveUnusedFiles_UsesNeutralConfirmationAndOneUndoGroup()
    {
        var audioPath = CreateTemporaryMediaFile("mp3", [1]);
        var videoPath = CreateTemporaryMediaFile("mp4", [2]);
        var dialogs = new RecordingDialogService();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(dialogService: dialogs);
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Cleanup");

        try
        {
            document.Audio.AddFile(audioPath, "unused audio.mp3");
            document.Video.AddFile(videoPath, "unused video.mp4");
            var linkCheck = await document.CheckLinksAsync();
            Assert.That(linkCheck.Item1.Select(item => item.Title),
                Has.Some.Contains("unused audio.mp3"));

            await document.Statistics.RemoveUnusedFiles.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(document.Audio.Files, Is.Empty);
                Assert.That(document.Video.Files, Is.Empty);
                Assert.That(dialogs.Confirmations, Has.Count.EqualTo(1));
                Assert.That(dialogs.Confirmations[0], Does.Contain("unused audio.mp3"));
                Assert.That(dialogs.Confirmations[0], Does.Contain("unused video.mp4"));
            });

            document.OperationsManager.Undo.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(document.Audio.Files.Select(item => item.Name), Is.EqualTo(new[] { "unused audio.mp3" }));
                Assert.That(document.Video.Files.Select(item => item.Name), Is.EqualTo(new[] { "unused video.mp4" }));
            });
        }
        finally
        {
            document.Dispose();
            File.Delete(audioPath);
            File.Delete(videoPath);
        }
    }

    [Test]
    public async Task ClosingDocument_CancelsPendingStatisticsPublicationWithoutReportingError()
    {
        var dispatcher = new BlockingUiDispatcher();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(uiDispatcher: dispatcher);
        using var package = TestHelper.CreateSimpleTestPackage();
        var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Closing validation");
        Exception? reportedError = null;
        document.Error += (exception, _) => reportedError = exception;

        _ = document.Statistics;
        await dispatcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        document.Dispose();
        await dispatcher.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(reportedError, Is.Null);
    }

    [Test]
    public async Task ClosingDocument_CancelsPendingUnusedFileConfirmationWithoutReportingError()
    {
        var sourcePath = CreateTemporaryMediaFile("mp3", [1]);
        var dialogs = new BlockingDialogService();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(dialogService: dialogs);
        using var package = TestHelper.CreateSimpleTestPackage();
        var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Closing cleanup");
        document.Audio.AddFile(sourcePath, "unused.mp3");
        Exception? reportedError = null;
        document.Error += (exception, _) => reportedError = exception;

        try
        {
            var cleanupTask = document.Statistics.RemoveUnusedFiles.ExecuteAsync(null);
            await dialogs.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            document.Dispose();
            await cleanupTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(dialogs.Cancelled.Task.IsCompletedSuccessfully, Is.True);
                Assert.That(reportedError, Is.Null);
            });
        }
        finally
        {
            document.Dispose();
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task RemoveUnusedFiles_RevalidatesReferencesAfterConfirmationBeforeDeleting()
    {
        var sourcePath = CreateTemporaryMediaFile("mp3", [1]);
        QDocument? document = null;
        var dialogs = new RecordingDialogService(() =>
        {
            var question = document!.Package.Rounds[0].Themes[0].Questions[0].Model;
            question.Script ??= new Script();
            question.Script.Steps.Add(new Step
            {
                Type = StepTypes.ShowContent,
                Parameters =
                {
                    [StepParameterNames.Content] = new StepParameter
                    {
                        Type = StepParameterTypes.Content,
                        ContentValue =
                        [
                            new ContentItem
                            {
                                Type = ContentTypes.Audio,
                                IsRef = true,
                                Value = "became-used.mp3",
                                Placement = ContentPlacements.Background,
                            },
                        ],
                    },
                },
            });
        });
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(dialogService: dialogs);
        using var package = TestHelper.CreateSimpleTestPackage();
        document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Revalidated cleanup");
        document.Audio.AddFile(sourcePath, "became-used.mp3");

        try
        {
            await document.Statistics.RemoveUnusedFiles.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(document.Audio.Files.Select(item => item.Name),
                    Is.EqualTo(new[] { "became-used.mp3" }));
                Assert.That(document.HasMediaReference(CollectionNames.AudioStorageName, "became-used.mp3"), Is.True);
            });
        }
        finally
        {
            document.Dispose();
            File.Delete(sourcePath);
        }
    }

    private static SIDocument CreateInvalidPackage()
    {
        var package = SIDocument.Create("Invalid", "Author");
        var round = new Round { Name = "Round" };
        var theme = new Theme { Name = "Theme" };
        theme.Questions.Add(new Question { Price = 200 });
        round.Themes.Add(theme);
        package.Package.Rounds.Add(round);
        return package;
    }

    private static string CreateTemporaryMediaFile(string extension, byte[] contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"siquester-statistics-{Guid.NewGuid():N}.{extension}");
        File.WriteAllBytes(path, contents);
        return path;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private sealed class RecordingDialogService(Action? onConfirm = null) : IDialogService
    {
        internal List<string> Confirmations { get; } = [];

        public ValueTask<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Confirmations.Add(message);
            onConfirm?.Invoke();
            return ValueTask.FromResult(true);
        }

        public ValueTask<SaveChangesDecision> ConfirmSaveChangesAsync(
            string message,
            bool allowCancel,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(SaveChangesDecision.Save);

        public ValueTask ShowMessageAsync(string message, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask ShowErrorAsync(string message, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<string?> SelectOptionAsync(
            string message,
            IReadOnlyList<DialogOption> options,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(null);
    }

    private sealed class BlockingUiDispatcher : IUiDispatcher
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                Cancelled.TrySetResult();
            }
        }
    }

    private sealed class BlockingDialogService : IDialogService
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            }
            finally
            {
                Cancelled.TrySetResult();
            }
        }

        public ValueTask<SaveChangesDecision> ConfirmSaveChangesAsync(
            string message,
            bool allowCancel,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(SaveChangesDecision.Save);

        public ValueTask ShowMessageAsync(string message, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask ShowErrorAsync(string message, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<string?> SelectOptionAsync(
            string message,
            IReadOnlyList<DialogOption> options,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(null);
    }
}
