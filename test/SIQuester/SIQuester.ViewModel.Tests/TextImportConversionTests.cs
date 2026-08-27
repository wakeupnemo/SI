using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using QTxtConverter;
using SIPackages;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Tests.Helpers;
using System.Text;
using Utils.Commands;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class TextImportConversionTests
{
    private const string RepresentativeSnsText = """
        Пакет 例

        Раунд 1

        Тема: История 例
        Автор: Автор темы

        10.
        Первый вопрос 例

        Ответ: Первый ответ
        Комментарий: Первый комментарий
        Источник: Источник 1

        20.
        Второй вопрос

        Ответ: Второй ответ
        Источник: Источник 2

        30.
        Третий вопрос

        Ответ: Третий ответ
        """;

    [Test]
    public void SnsFixture_ConvertsIntoRepresentativeSemanticPackage()
    {
        var converter = new QConverter();
        var parts = converter.ExtractQuestions(RepresentativeSnsText);

        Assert.That(parts, Is.Not.Null);
        Assert.That(parts, Has.Length.GreaterThan(1));

        var templates = QConverter.GetSnsTemplates(parts!, standartLogic: true);
        ReadErrorEventArgs? readError = null;
        converter.ReadError += (_, error) =>
        {
            readError = error;
            error.Cancel = true;
        };
        SIDocument document = null!;

        try
        {
            var converted = converter.ReadFile(
                parts!,
                templates,
                ref document,
                addToExisting: false,
                "Импортированный пакет 例",
                "Автор импорта",
                "Пустой раунд",
                out var themeCount);

            Assert.That(
                converted,
                Is.True,
                $"Read error at {readError?.Index}, move {readError?.Move}");

            Assert.Multiple(() =>
            {
                Assert.That(themeCount, Is.EqualTo(1));
                Assert.That(document, Is.Not.Null);
                Assert.That(document.Package.Rounds, Has.Count.EqualTo(1));
                Assert.That(document.Package.Rounds[0].Themes, Has.Count.EqualTo(1));
                Assert.That(document.Package.Rounds[0].Themes[0].Questions, Has.Count.EqualTo(3));
            });

            AssertRepresentativeSemantics(document, "Автор импорта");
        }
        finally
        {
            document?.Dispose();
        }
    }

    [Test]
    public void Converter_RejectsPreCancelledSplit()
    {
        var converter = new QConverter();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => converter.ExtractQuestions(RepresentativeSnsText, cancellationSource.Token));
    }

    [Test]
    public async Task RunWithoutQuestionSequence_UsesNeutralDialogAndCompletes()
    {
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(dialogService: dialogService);
        using var workspace = new ImportTextViewModel(
            new AppOptions(),
            serviceProvider.GetRequiredService<IClipboardService>(),
            serviceProvider.GetRequiredService<IDocumentViewModelFactory>(),
            serviceProvider.GetRequiredService<IFilePickerService>(),
            dialogService,
            serviceProvider.GetRequiredService<IUiDispatcher>())
        {
            Text = "Текст без последовательности нумерованных вопросов",
        };

        await ((IAsyncCommand)workspace.Run).ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));

        await dialogService.Received(1).ShowMessageAsync(
            Arg.Is<string>(message => !string.IsNullOrWhiteSpace(message)),
            Arg.Any<CancellationToken>());
        Assert.That(workspace.Run.CanExecute(null), Is.True);
    }

    [Test]
    public async Task WorkspaceCommands_ConvertSaveAndReloadRepresentativeTextPackage()
    {
        var picker = new RepresentativeTextPicker(RepresentativeSnsText);
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        var previousContext = SynchronizationContext.Current;
        QDocument? convertedDocument = null;
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"SIQuester text import {Guid.NewGuid():N} 例",
            "Импортированный пакет 例.siq");

        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            using var workspace = new ImportTextViewModel(
                new AppOptions(),
                serviceProvider.GetRequiredService<IClipboardService>(),
                serviceProvider.GetRequiredService<IDocumentViewModelFactory>(),
                picker,
                serviceProvider.GetRequiredService<IDialogService>(),
                serviceProvider.GetRequiredService<IUiDispatcher>());
            var producedDocument = new TaskCompletionSource<QDocument>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? reportedError = null;
            workspace.NewItem += item =>
            {
                if (item is QDocument document)
                {
                    producedDocument.TrySetResult(document);
                }
            };
            workspace.Error += (exception, _) => reportedError = exception;

            await ((IAsyncCommand)workspace.SelectFile).ExecuteAsync(null);
            workspace.ApproveImport.Execute(null);
            await ((IAsyncCommand)workspace.Run).ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
            workspace.Sns.Execute(null);
            workspace.Go.Execute(null);
            convertedDocument = await producedDocument.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(reportedError, Is.Null);
            AssertRepresentativeSemantics(convertedDocument.Document, expectedAuthor: null);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            convertedDocument.Path = outputPath;
            await convertedDocument.Save.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(outputPath), Is.True);
                Assert.That(File.Exists(outputPath + ".bak"), Is.False);
                Assert.That(convertedDocument.Changed, Is.False);
            });

            await using var input = File.OpenRead(outputPath);
            using var reloaded = SIDocument.Load(input);
            AssertRepresentativeSemantics(reloaded, expectedAuthor: null);
        }
        finally
        {
            convertedDocument?.Dispose();
            SynchronizationContext.SetSynchronizationContext(previousContext);

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (outputDirectory != null && Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static void AssertRepresentativeSemantics(SIDocument document, string? expectedAuthor)
    {
        var package = document.Package;
        var round = package.Rounds.Single();
        var theme = round.Themes.Single();
        var questions = theme.Questions;

        Assert.Multiple(() =>
        {
            Assert.That(package.Name, Is.EqualTo("Импортированный пакет 例"));
            Assert.That(package.Info.Authors, Has.Count.EqualTo(1));
            Assert.That(package.Info.Authors[0], Is.Not.Empty);
            Assert.That(package.Info.Comments.Text, Does.Contain("Пакет 例"));
            Assert.That(round.Name, Is.EqualTo("Раунд 1"));
            Assert.That(theme.Name, Is.EqualTo("История 例"));
            Assert.That(theme.Info.Authors, Does.Contain("Автор темы"));
            Assert.That(questions.Select(question => question.Price), Is.EqualTo(new[] { 10, 20, 30 }));
            Assert.That(questions.Select(question => question.GetText()), Is.EqualTo(new[]
            {
                "Первый вопрос 例",
                "Второй вопрос",
                "Третий вопрос",
            }));
            Assert.That(questions.Select(question => question.Right.Single()), Is.EqualTo(new[]
            {
                "Первый ответ",
                "Второй ответ",
                "Третий ответ",
            }));
            Assert.That(questions[0].Info.Comments.Text, Is.EqualTo("Первый комментарий"));
            Assert.That(questions[0].Info.Sources, Is.EqualTo(new[] { "Источник 1" }));
            Assert.That(questions[1].Info.Sources, Is.EqualTo(new[] { "Источник 2" }));
            Assert.That(questions[2].Info.Sources, Is.Empty);
            Assert.That(questions[1].Info.Comments.Text, Is.Empty);
            Assert.That(questions[2].Info.Comments.Text, Is.Empty);
        });

        if (expectedAuthor != null)
        {
            Assert.That(package.Info.Authors, Does.Contain(expectedAuthor));
        }
    }

    private sealed class RepresentativeTextPicker(string text) : IFilePickerService
    {
        public ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
            OpenFilePickerRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                .GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(text))
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<PickedFile>>(
                [new PickedFile(
                    null,
                    "Импортированный пакет 例.txt",
                    ".txt",
                    token =>
                    {
                        token.ThrowIfCancellationRequested();
                        return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
                    })]);
        }

        public ValueTask<PickedFile?> PickSaveFileAsync(
            SaveFilePickerRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<PickedFile?>(null);
    }
}
