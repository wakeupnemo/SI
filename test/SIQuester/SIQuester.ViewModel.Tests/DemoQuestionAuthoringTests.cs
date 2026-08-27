using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Model;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class DemoQuestionAuthoringTests
{
    private static readonly byte[] ImageBytes = [1, 3, 3, 7];
    private static readonly byte[] AudioBytes = [2, 4, 6, 8];
    private static readonly byte[] VideoBytes = [9, 7, 5, 3];
    private static readonly byte[] HtmlBytes = "<p>demo 例</p>"u8.ToArray();

    [Test]
    public async Task DemoBehaviors_CreatedFromNewPackageCommands_SaveAndReloadCanonicalSemantics()
    {
        var picker = new QueueFilePicker(
            CreateFile("question 例.png", ImageBytes),
            CreateFile("question 例.mp3", AudioBytes),
            CreateFile("question 例.mp4", VideoBytes),
            CreateFile("question 例.html", HtmlBytes),
            CreateFile("answer 例.png", ImageBytes),
            CreateFile("answer 例.mp3", AudioBytes),
            CreateFile("answer 例.mp4", VideoBytes));
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(picker);
        using var package = SIDocument.Create("Demo authoring 例", "Author");
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Demo authoring");
        var previousCreateQuestions = AppSettings.Default.CreateQuestionsWithTheme;
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester v0.2 demo {Guid.NewGuid():N} 例.siq");

        try
        {
            AppSettings.Default.CreateQuestionsWithTheme = false;
            document.Package.AddRound.Execute(null);
            var round = document.Package.Rounds.Single();
            round.AddTheme.Execute(null);
            var theme = round.Themes.Single();

            var roundDefault = AddQuestion(theme);
            roundDefault.ClearType.Execute(null);
            roundDefault.QuestionText = "round-default";

            var simple = AddQuestion(theme);
            simple.SetQuestionType.Execute(QuestionTypes.Simple);
            simple.QuestionText = "simple";

            var stake = AddQuestion(theme);
            stake.SetQuestionType.Execute(QuestionTypes.Stake);
            stake.QuestionText = "stake";
            stake.Right[0] = "right one";
            stake.Right.Add("right two");
            stake.Wrong.Add("wrong one");

            var stakeAll = AddQuestion(theme);
            stakeAll.SetQuestionType.Execute(QuestionTypes.StakeAll);
            stakeAll.QuestionText = "stake-all";

            var fixedSecret = AddQuestion(theme);
            ConfigureSecret(fixedSecret, QuestionTypes.Secret, "Fixed theme", NumberSetMode.FixedValue, 2000, 2000, 0, false);

            var roundRangeSecret = AddQuestion(theme);
            ConfigureSecret(roundRangeSecret, QuestionTypes.Secret, "Round extremes", NumberSetMode.MinimumOrMaximumInRound, 0, 0, 0, false);

            var steppedSecret = AddQuestion(theme);
            ConfigureSecret(steppedSecret, QuestionTypes.Secret, "Stepped theme", NumberSetMode.RangeWithStep, 100, 1000, 200, true);

            var publicPriceSecret = AddQuestion(theme);
            ConfigureSecret(publicPriceSecret, QuestionTypes.SecretPublicPrice, "Public theme", NumberSetMode.Range, 100, 500, 400, false);

            var noQuestionSecret = AddQuestion(theme);
            noQuestionSecret.SetQuestionType.Execute(QuestionTypes.SecretNoQuestion);
            noQuestionSecret.SecretPrice!.Mode = NumberSetMode.FixedValue;
            noQuestionSecret.SecretPrice.Minimum = 700;
            noQuestionSecret.QuestionText = string.Empty;

            var noRisk = AddQuestion(theme);
            noRisk.SetQuestionType.Execute(QuestionTypes.NoRisk);
            noRisk.QuestionText = "no-risk";

            var forAll = AddQuestion(theme);
            forAll.SetQuestionType.Execute(QuestionTypes.ForAll);
            forAll.QuestionText = "for-all";

            var custom = AddQuestion(theme);
            custom.SetQuestionType.Execute("custom");
            custom.QuestionText = "custom-manual";

            var emptyCell = AddQuestion(theme);
            emptyCell.ClearType.Execute(null);
            emptyCell.Model.Price = Question.InvalidPrice;
            emptyCell.QuestionText = string.Empty;
            emptyCell.Right[0] = string.Empty;

            var numeric = AddQuestion(theme);
            numeric.QuestionText = "numeric";
            numeric.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Number);
            numeric.NumericAnswer!.Answer = 100;
            numeric.NumericAnswer.Deviation = 5;

            var point = AddQuestion(theme);
            point.QuestionText = "point";
            point.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Point);
            point.PointAnswer!.Answer = "0.46,0.7,1.33";
            point.PointAnswer.Deviation = 0.05;

            var select = AddQuestion(theme);
            select.QuestionText = "select";
            select.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Select);
            var options = select.AnswerOptions!.GroupValue!;

            while (options.Count < 4)
            {
                options.AddItem.Execute(select);
            }

            while (options.Count > 4)
            {
                options.DeleteItem.Execute(options[^1]);
            }

            var optionLabels = new[] { "A", "B", "C", "D" };

            for (var i = 0; i < optionLabels.Length; i++)
            {
                SetOption(options[i], optionLabels[i]);
            }

            options.MakeRight.Execute(options[2]);

            var client = AddQuestion(theme);
            client.QuestionText = "client";
            client.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_ManagedByClient);

            var timed = AddQuestion(theme);
            timed.QuestionText = "timed";
            timed.SetAnswerTime.Execute(null);
            timed.AnswerDuration = 15;
            timed.SetQuestionType.Execute(QuestionTypes.Stake);

            var orderedContent = AddQuestion(theme);
            orderedContent.QuestionText = "replic first";
            var content = orderedContent.LegacyContent!;
            content[0].Model.Placement = ContentPlacements.Replic;
            content[0].Model.WaitForFinish = false;
            content.CurrentPosition = 0;
            content.AddText.Execute(null);
            content.CurrentItem!.Model.Value = "timed screen";
            content.CurrentItem.DurationSeconds = 8;
            await AddMediaAsync(content, ContentTypes.Image);
            content.CurrentItem!.Model.WaitForFinish = false;
            await AddMediaAsync(content, ContentTypes.Audio);
            await AddMediaAsync(content, ContentTypes.Video);
            await AddMediaAsync(content, ContentTypes.Html);
            content.CurrentItem!.DurationSeconds = 5;

            var postImage = AddQuestion(theme);
            await AddPostAnswerMediaAsync(postImage, ContentTypes.Image);
            var postAudio = AddQuestion(theme);
            await AddPostAnswerMediaAsync(postAudio, ContentTypes.Audio);
            var postVideo = AddQuestion(theme);
            await AddPostAnswerMediaAsync(postVideo, ContentTypes.Video);

            document.Package.AddRound.Execute(null);
            var finalRound = document.Package.Rounds[^1];
            finalRound.Model.Type = RoundTypes.Final;
            finalRound.AddTheme.Execute(null);
            var finalQuestion = AddQuestion(finalRound.Themes.Single());
            finalQuestion.ClearType.Execute(null);
            finalQuestion.QuestionText = "final-default";

            document.Path = filePath;
            await document.Save.ExecuteAsync(null);

            await using var savedStream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(savedStream);
            var questions = reloaded.Package.Rounds[0].Themes[0].Questions;

            Assert.Multiple(() =>
            {
                Assert.That(questions.Take(13).Select(question => question.TypeName), Is.EqualTo(new[]
                {
                    QuestionTypes.Default,
                    QuestionTypes.Simple,
                    QuestionTypes.Stake,
                    QuestionTypes.StakeAll,
                    QuestionTypes.Secret,
                    QuestionTypes.Secret,
                    QuestionTypes.Secret,
                    QuestionTypes.SecretPublicPrice,
                    QuestionTypes.SecretNoQuestion,
                    QuestionTypes.NoRisk,
                    QuestionTypes.ForAll,
                    "custom",
                    QuestionTypes.Default,
                }));
                Assert.That(questions[2].Right, Is.EqualTo(new[] { "right one", "right two" }));
                Assert.That(questions[2].Wrong, Is.EqualTo(new[] { "wrong one" }));
                AssertSecret(questions[4], "Fixed theme", 2000, 2000, 0, StepParameterValues.SetAnswererSelect_ExceptCurrent);
                AssertSecret(questions[5], "Round extremes", 0, 0, 0, StepParameterValues.SetAnswererSelect_ExceptCurrent);
                AssertSecret(questions[6], "Stepped theme", 100, 1000, 200, StepParameterValues.SetAnswererSelect_Any);
                AssertSecret(questions[7], "Public theme", 100, 500, 400, StepParameterValues.SetAnswererSelect_ExceptCurrent);
                Assert.That(questions[8].Parameters[QuestionParameterNames.Price].NumberSetValue,
                    Is.EqualTo(new NumberSet(700)));
                Assert.That(questions[8].Parameters.ContainsKey(QuestionParameterNames.Theme), Is.False);
                Assert.That(questions[12].Price, Is.EqualTo(Question.InvalidPrice));
                Assert.That(questions[13].Parameters[QuestionParameterNames.AnswerType].SimpleValue,
                    Is.EqualTo(StepParameterValues.SetAnswerTypeType_Number));
                Assert.That(questions[13].Parameters[QuestionParameterNames.AnswerDeviation].SimpleValue, Is.EqualTo("5"));
                Assert.That(questions[14].Right, Is.EqualTo(new[] { "0.46,0.7,1.33" }));
                Assert.That(questions[14].Parameters[QuestionParameterNames.AnswerDeviation].SimpleValue, Is.EqualTo("0.05"));
                Assert.That(questions[15].Parameters[QuestionParameterNames.AnswerOptions].GroupValue!.Values
                    .Select(option => option.ContentValue![0].Value), Is.EqualTo(new[] { "A", "B", "C", "D" }));
                Assert.That(questions[15].Right, Is.EqualTo(new[] { "C" }));
                Assert.That(questions[16].Parameters[QuestionParameterNames.AnswerType].SimpleValue,
                    Is.EqualTo(StepParameterValues.SetAnswerTypeType_ManagedByClient));
                Assert.That(questions[17].TypeName, Is.EqualTo(QuestionTypes.Stake));
                Assert.That(questions[17].Parameters[QuestionParameterNames.AnswerDuration].SimpleValue, Is.EqualTo("15"));
                Assert.That(reloaded.Package.Rounds[1].Type, Is.EqualTo(RoundTypes.Final));
                Assert.That(reloaded.Package.Rounds[1].Themes[0].Questions[0].TypeName, Is.Empty);
            });

            var ordered = questions[18].Parameters[QuestionParameterNames.Question].ContentValue!;
            Assert.Multiple(() =>
            {
                Assert.That(ordered.Select(item => item.Type), Is.EqualTo(new[]
                {
                    ContentTypes.Text,
                    ContentTypes.Text,
                    ContentTypes.Image,
                    ContentTypes.Audio,
                    ContentTypes.Video,
                    ContentTypes.Html,
                }));
                Assert.That(ordered[0].Placement, Is.EqualTo(ContentPlacements.Replic));
                Assert.That(ordered[0].WaitForFinish, Is.False);
                Assert.That(ordered[1].Duration, Is.EqualTo(TimeSpan.FromSeconds(8)));
                Assert.That(ordered[2].WaitForFinish, Is.False);
                Assert.That(ordered[5].Duration, Is.EqualTo(TimeSpan.FromSeconds(5)));
                AssertPostAnswer(questions[19], ContentTypes.Image, "answer 例.png");
                AssertPostAnswer(questions[20], ContentTypes.Audio, "answer 例.mp3");
                AssertPostAnswer(questions[21], ContentTypes.Video, "answer 例.mp4");
                Assert.That(ReadBytes(reloaded.Images, "question 例.png"), Is.EqualTo(ImageBytes));
                Assert.That(ReadBytes(reloaded.Audio, "question 例.mp3"), Is.EqualTo(AudioBytes));
                Assert.That(ReadBytes(reloaded.Video, "question 例.mp4"), Is.EqualTo(VideoBytes));
                Assert.That(ReadBytes(reloaded.Html, "question 例.html"), Is.EqualTo(HtmlBytes));
                Assert.That(ReadBytes(reloaded.Images, "answer 例.png"), Is.EqualTo(ImageBytes));
                Assert.That(ReadBytes(reloaded.Audio, "answer 例.mp3"), Is.EqualTo(AudioBytes));
                Assert.That(ReadBytes(reloaded.Video, "answer 例.mp4"), Is.EqualTo(VideoBytes));
            });

            Assert.That(picker.RequestCount, Is.EqualTo(7));
            Assert.That(ScriptsLibrary.Scripts.Keys, Is.SupersetOf(new[]
            {
                QuestionTypes.Simple,
                QuestionTypes.Stake,
                QuestionTypes.StakeAll,
                QuestionTypes.Secret,
                QuestionTypes.SecretPublicPrice,
                QuestionTypes.SecretNoQuestion,
                QuestionTypes.NoRisk,
                QuestionTypes.ForAll,
            }));
        }
        finally
        {
            AppSettings.Default.CreateQuestionsWithTheme = previousCreateQuestions;
            File.Delete(filePath);
        }
    }

    [Test]
    public void UnknownQuestionTypeAndParameters_AreUntouchedUntilKnownBehaviorIsExplicitlySelected()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = SIDocument.Create("Future behavior", "Author");
        var question = new Question { Price = 100, TypeName = "future-secret-v9" };
        question.Parameters["futureParameter"] = new StepParameter
        {
            Type = "futureKind",
            SimpleValue = "opaque 例",
        };
        question.Right.Add("answer");
        var theme = new Theme { Name = "Theme", Questions = { question } };
        package.Package.Rounds.Add(new Round { Name = "Round", Themes = { theme } });
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Future behavior");
        var viewModel = document.Package.Rounds[0].Themes[0].Questions[0];

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.TypeName, Is.EqualTo("future-secret-v9"));
            Assert.That(viewModel.Parameters.Model["futureParameter"].Type, Is.EqualTo("futureKind"));
            Assert.That(viewModel.Parameters.Model["futureParameter"].SimpleValue, Is.EqualTo("opaque 例"));
            Assert.That(document.OperationsManager.Undo.CanBeExecuted, Is.False);
        });

        viewModel.SetQuestionType.Execute(QuestionTypes.Stake);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.TypeName, Is.EqualTo(QuestionTypes.Stake));
            Assert.That(viewModel.Parameters.Model.ContainsKey("futureParameter"), Is.False);
            Assert.That(document.OperationsManager.Undo.CanBeExecuted, Is.True);
        });
    }

    [Test]
    public void SecretBehaviorSwitchAndEndpointEdits_PreserveCompatibleSettingsAndUndoAsSingleChanges()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = SIDocument.Create("Secret transitions", "Author");
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Secret transitions");
        var previousCreateQuestions = AppSettings.Default.CreateQuestionsWithTheme;

        try
        {
            AppSettings.Default.CreateQuestionsWithTheme = false;
            document.Package.AddRound.Execute(null);
            var round = document.Package.Rounds.Single();
            round.AddTheme.Execute(null);
            var question = AddQuestion(round.Themes.Single());
            question.SetQuestionType.Execute(QuestionTypes.Secret);
            question.SecretThemeParameter!.Model.SimpleValue = "Preserved theme 例";
            question.SecretPrice!.Mode = NumberSetMode.Range;
            question.SecretPrice.Minimum = 100;
            question.SecretPrice.Maximum = 500;
            question.SecretAllowsCurrentPlayer = true;
            question.AnswerDuration = 15;

            question.SetQuestionType.Execute(QuestionTypes.SecretPublicPrice);

            Assert.Multiple(() =>
            {
                Assert.That(question.TypeName, Is.EqualTo(QuestionTypes.SecretPublicPrice));
                Assert.That(question.SecretThemeParameter!.Model.SimpleValue, Is.EqualTo("Preserved theme 例"));
                Assert.That(question.SecretAllowsCurrentPlayer, Is.True);
                Assert.That(question.SecretPrice!.Minimum, Is.EqualTo(100));
                Assert.That(question.SecretPrice.Maximum, Is.EqualTo(500));
                Assert.That(question.SecretPrice.Step, Is.EqualTo(400));
                Assert.That(question.AnswerDuration, Is.EqualTo(15));
            });

            document.OperationsManager.Undo.Execute(null);
            Assert.That(question.TypeName, Is.EqualTo(QuestionTypes.Secret));
            Assert.That(question.SecretPrice!.Step, Is.EqualTo(400));
            document.OperationsManager.Redo.Execute(null);
            Assert.That(question.TypeName, Is.EqualTo(QuestionTypes.SecretPublicPrice));

            question.SecretPrice!.Maximum = 700;
            Assert.That(question.SecretPrice.Step, Is.EqualTo(600));
            document.OperationsManager.Undo.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(question.SecretPrice.Maximum, Is.EqualTo(500));
                Assert.That(question.SecretPrice.Step, Is.EqualTo(400));
                Assert.That(question.SecretThemeParameter!.Model.SimpleValue, Is.EqualTo("Preserved theme 例"));
                Assert.That(question.AnswerDuration, Is.EqualTo(15));
            });
        }
        finally
        {
            AppSettings.Default.CreateQuestionsWithTheme = previousCreateQuestions;
        }
    }

    private static QuestionViewModel AddQuestion(ThemeViewModel theme)
    {
        theme.AddQuestion.Execute(null);
        return theme.Questions[^1];
    }

    private static void ConfigureSecret(
        QuestionViewModel question,
        string type,
        string theme,
        NumberSetMode mode,
        int minimum,
        int maximum,
        int step,
        bool allowCurrent)
    {
        question.SetQuestionType.Execute(type);
        question.SecretThemeParameter!.Model.SimpleValue = theme;
        question.SecretPrice!.Mode = mode;
        question.SecretPrice.Minimum = minimum;
        question.SecretPrice.Maximum = maximum;
        question.SecretPrice.Step = step;
        question.SecretAllowsCurrentPlayer = allowCurrent;
    }

    private static void SetOption(StepParameterRecord option, string text)
    {
        option.Value.ContentValue!.CurrentPosition = 0;
        option.Value.ContentValue.CurrentItem!.Model.Value = text;
    }

    private static async Task AddMediaAsync(ContentItemsViewModel content, string contentType)
    {
        content.CurrentPosition = content.Count - 1;
        await content.AddFile.ExecuteAsync(contentType);
        content.CurrentPosition = content.Count - 1;
    }

    private static async Task AddPostAnswerMediaAsync(QuestionViewModel question, string contentType)
    {
        question.AddComplexAnswer.Execute(null);
        var answerContent = question.PostAnswerContent!;
        answerContent.CurrentPosition = 0;
        await answerContent.AddFile.ExecuteAsync(contentType);
    }

    private static void AssertSecret(
        Question question,
        string theme,
        int minimum,
        int maximum,
        int step,
        string selectionMode)
    {
        Assert.That(question.Parameters[QuestionParameterNames.Theme].SimpleValue, Is.EqualTo(theme));
        Assert.That(question.Parameters[QuestionParameterNames.SelectionMode].SimpleValue, Is.EqualTo(selectionMode));
        Assert.That(question.Parameters[QuestionParameterNames.Price].NumberSetValue, Is.EqualTo(new NumberSet
        {
            Minimum = minimum,
            Maximum = maximum,
            Step = step,
        }));
    }

    private static void AssertPostAnswer(Question question, string contentType, string fileName)
    {
        var answer = question.Parameters[QuestionParameterNames.Answer].ContentValue!;
        Assert.That(answer, Has.Count.EqualTo(1));
        Assert.That(answer[0].Type, Is.EqualTo(contentType));
        Assert.That(answer[0].Value, Is.EqualTo(fileName));
        Assert.That(answer[0].IsRef, Is.True);
    }

    private static PickedFile CreateFile(string name, byte[] bytes) => new(
        localPath: null,
        displayName: name,
        extension: Path.GetExtension(name),
        _ => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)));

    private static byte[] ReadBytes(DataCollection collection, string name)
    {
        using var stream = collection.GetFile(name)!.Stream;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed class QueueFilePicker(params PickedFile[] files) : IFilePickerService
    {
        private readonly Queue<PickedFile> _files = new(files);

        internal int RequestCount { get; private set; }

        public ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
            OpenFilePickerRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return ValueTask.FromResult<IReadOnlyList<PickedFile>>([_files.Dequeue()]);
        }

        public ValueTask<PickedFile?> PickSaveFileAsync(
            SaveFilePickerRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
