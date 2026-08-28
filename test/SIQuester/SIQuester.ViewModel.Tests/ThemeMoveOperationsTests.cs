using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Serializers;
using SIQuester.ViewModel.Services;
using SIQuester.ViewModel.Tests.Helpers;
using System.Text;
using System.Xml;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class ThemeMoveOperationsTests
{
    private IServiceProvider _serviceProvider = null!;
    private IDocumentViewModelFactory _documentFactory = null!;
    private int _originalQuestionBase;

    [SetUp]
    public void SetUp()
    {
        _serviceProvider = TestHelper.CreateServiceProvider();
        _documentFactory = _serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        _originalQuestionBase = AppSettings.Default.QuestionBase;
        AppSettings.Default.QuestionBase = 100;
    }

    [TearDown]
    public void TearDown()
    {
        AppSettings.Default.QuestionBase = _originalQuestionBase;

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [Test]
    public async Task MoveAcrossRounds_PreservesThemeSemanticsAndMediaAndIsUndoableAfterRoundTrip()
    {
        const string imageName = "изображение 例.png";
        var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };
        using var package = SIDocument.Create("Theme move", "Test author");

        var sourceRound = new Round { Name = "Round one" };
        var targetRound = new Round { Name = "Раунд два" };
        var theme = CreateRichTheme(imageName);
        var existingTargetTheme = new Theme { Name = "Existing target" };
        existingTargetTheme.Questions.Add(CreateQuestion("X", 600));
        sourceRound.Themes.Add(theme);
        targetRound.Themes.Add(existingTargetTheme);
        package.Package.Rounds.Add(sourceRound);
        package.Package.Rounds.Add(targetRound);

        using var document = _documentFactory.CreateViewModelFor(package, "Theme move");
        var mediaSourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"theme move source {Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(mediaSourcePath, imageBytes);
        document.Images.AddFile(mediaSourcePath, imageName);
        var sourceTheme = document.Package.Rounds[0].Themes[0];
        document.Package.IsSelected = false;
        sourceTheme.IsSelected = true;
        document.ActiveNode = sourceTheme;
        var expectedMovedTheme = sourceTheme.Model.Clone();
        expectedMovedTheme.Questions[0].Price = 200;
        expectedMovedTheme.Questions[1].Price = 400;
        var dragData = document.ThemeMoves.CreateDragData(sourceTheme);

        var result = document.ThemeMoves.Apply(
            dragData,
            new ThemeLocation(1, 1),
            recalculatePrices: true);

        var movedTheme = document.Package.Rounds[1].Themes[1];
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(ThemeMoveResult.Applied));
            Assert.That(document.Package.Rounds[0].Themes, Is.Empty);
            Assert.That(Serialize(movedTheme.Model), Is.EqualTo(Serialize(expectedMovedTheme)));
            Assert.That(movedTheme.Model, Is.Not.SameAs(sourceTheme.Model));
            Assert.That(document.Package.Rounds[1].Model.Themes[1], Is.SameAs(movedTheme.Model));
            Assert.That(movedTheme.OwnerRound, Is.SameAs(document.Package.Rounds[1]));
            Assert.That(document.ActiveNode, Is.SameAs(movedTheme));
            Assert.That(document.OperationsManager.Undo.CanExecute(null), Is.True);
            Assert.That(document.Images.Files.Select(file => file.Model.Name), Does.Contain(imageName));
        });

        document.OperationsManager.Undo.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(document.Package.Rounds[0].Themes, Has.Count.EqualTo(1));
            Assert.That(document.Package.Rounds[0].Themes[0], Is.SameAs(sourceTheme));
            Assert.That(Serialize(document.Package.Rounds[0].Themes[0].Model), Is.EqualTo(Serialize(theme)));
            Assert.That(document.Package.Rounds[1].Themes, Has.Count.EqualTo(1));
            Assert.That(document.ActiveNode, Is.SameAs(sourceTheme));
            Assert.That(sourceTheme.IsSelected, Is.True);
            Assert.That(document.ActiveChain,
                Is.EqualTo(new IItemViewModel[] { document.Package, document.Package.Rounds[0], sourceTheme }));
        });

        document.OperationsManager.Redo.Execute(null);
        Assert.That(Serialize(document.Package.Rounds[1].Themes[1].Model), Is.EqualTo(Serialize(expectedMovedTheme)));
        Assert.That(document.Images.Files.Select(file => file.Model.Name), Does.Contain(imageName));
        Assert.That(document.ActiveNode, Is.SameAs(movedTheme));
        Assert.That(document.ActiveChain,
            Is.EqualTo(new IItemViewModel[] { document.Package, document.Package.Rounds[1], movedTheme }));

        var outputPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"theme move Юникод {Guid.NewGuid():N}.siq");

        try
        {
            await document.SaveAsInternalAsync(outputPath);

            using var stream = File.OpenRead(outputPath);
            using var reloaded = SIDocument.Load(stream);
            var reloadedTheme = reloaded.Package.Rounds[1].Themes[1];
            var media = reloaded.Images.GetFile(imageName);

            Assert.Multiple(() =>
            {
                Assert.That(Serialize(reloadedTheme), Is.EqualTo(Serialize(expectedMovedTheme)));
                Assert.That(media, Is.Not.Null);
            });

            await using var mediaStream = media!.Stream;
            using var copiedMedia = new MemoryStream();
            await mediaStream.CopyToAsync(copiedMedia);
            Assert.That(copiedMedia.ToArray(), Is.EqualTo(imageBytes));
        }
        finally
        {
            File.Delete(outputPath);
            File.Delete(mediaSourcePath);
        }
    }

    [Test]
    public void MoveWithinRound_ReordersCanonicalThemesAndNormalizesMovedThemePricesInOneUndoStep()
    {
        using var document = CreateDocument(
            ("First", [100, 200]),
            ("Moved", [17, Question.InvalidPrice, 49]),
            ("Last", [300]));
        var round = document.Package.Rounds[0];
        var movedTheme = round.Themes[1];
        var dragData = document.ThemeMoves.CreateDragData(movedTheme);

        var result = document.ThemeMoves.Apply(
            dragData,
            new ThemeLocation(0, 3),
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(ThemeMoveResult.Applied));
            Assert.That(round.Themes.Select(item => item.Model.Name), Is.EqualTo(new[] { "First", "Last", "Moved" }));
            Assert.That(round.Model.Themes.Select(item => item.Name), Is.EqualTo(new[] { "First", "Last", "Moved" }));
            Assert.That(Prices(movedTheme), Is.EqualTo(new[] { 100, 200, 300 }));
            Assert.That(round.Themes[2], Is.SameAs(movedTheme));
        });

        document.OperationsManager.Undo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(round.Themes.Select(item => item.Model.Name), Is.EqualTo(new[] { "First", "Moved", "Last" }));
            Assert.That(Prices(round.Themes[1]), Is.EqualTo(new[] { 17, Question.InvalidPrice, 49 }));
        });

        document.OperationsManager.Redo.Execute(null);
        Assert.That(round.Themes.Select(item => item.Model.Name), Is.EqualTo(new[] { "First", "Last", "Moved" }));
    }

    [Test]
    public void MoveAcrossRounds_WithoutRecalculationKeepsAllPricesWithTheme()
    {
        using var document = CreateTwoRoundDocument([125, Question.InvalidPrice, 725]);
        var sourceTheme = document.Package.Rounds[0].Themes[0];
        var dragData = document.ThemeMoves.CreateDragData(sourceTheme);

        var result = document.ThemeMoves.Apply(
            dragData,
            new ThemeLocation(1, 0),
            recalculatePrices: false);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(ThemeMoveResult.Applied));
            Assert.That(Prices(document.Package.Rounds[1].Themes[0]),
                Is.EqualTo(new[] { 125, Question.InvalidPrice, 725 }));
        });
    }

    [Test]
    public async Task MoveAcrossRounds_TracksClonedQuestionEditsAndDetachesRemovedSourceTree()
    {
        using var document = CreateTwoRoundDocument([125, 250]);
        var sourceTheme = document.Package.Rounds[0].Themes[0];
        var result = document.ThemeMoves.Apply(
            document.ThemeMoves.CreateDragData(sourceTheme),
            new ThemeLocation(1, 0),
            recalculatePrices: true);
        var movedQuestion = document.Package.Rounds[1].Themes[0].Questions[0];
        var movedPrice = movedQuestion.Model.Price;
        var outputPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"tracked moved theme {Guid.NewGuid():N}.siq");

        try
        {
            await document.SaveAsInternalAsync(outputPath);
            Assert.That(result, Is.EqualTo(ThemeMoveResult.Applied));
            Assert.That(document.Changed, Is.False);

            sourceTheme.Questions[0].Model.Price = 999;
            sourceTheme.Questions[0].Info.Comments.Text = "Detached edit";
            Assert.That(document.Changed, Is.False, "Removed source descendants must not retain document listeners");

            movedQuestion.Model.Price = 777;
            Assert.Multiple(() =>
            {
                Assert.That(document.Changed, Is.True, "The inserted clone must participate in dirty tracking");
                Assert.That(document.OperationsManager.Undo.CanExecute(null), Is.True);
            });

            document.OperationsManager.Undo.Execute(null);
            Assert.That(movedQuestion.Model.Price, Is.EqualTo(movedPrice));

            await document.SaveInternalAsync();
            movedQuestion.Info.Comments.Text = "Tracked nested edit";
            Assert.That(document.Changed, Is.True, "Nested metadata on the inserted clone must remain tracked");

            document.OperationsManager.Undo.Execute(null);
            Assert.That(movedQuestion.Info.Comments.Text, Is.Empty);

            document.OperationsManager.Undo.Execute(null);
            await document.SaveInternalAsync();
            movedQuestion.Model.Price = movedPrice + 1;
            Assert.That(document.Changed, Is.False, "Undo must detach the removed clone subtree");
            movedQuestion.Model.Price = movedPrice;

            document.OperationsManager.Redo.Execute(null);
            await document.SaveInternalAsync();
            movedQuestion.Right.Add("Tracked after redo");
            Assert.That(document.Changed, Is.True, "Redo must reattach the clone subtree exactly once");

            document.OperationsManager.Undo.Execute(null);
            Assert.That(movedQuestion.Right, Does.Not.Contain("Tracked after redo"));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Test]
    public void MoveIntoFinalRound_SetsEveryMovedQuestionPriceToZeroAndIsUndoable()
    {
        using var document = CreateTwoRoundDocument(
            [125, Question.InvalidPrice, 725],
            RoundTypes.Final);
        var sourceTheme = document.Package.Rounds[0].Themes[0];
        var originalPrices = Prices(sourceTheme);

        var result = document.ThemeMoves.Apply(
            document.ThemeMoves.CreateDragData(sourceTheme),
            new ThemeLocation(1, 0),
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(ThemeMoveResult.Applied));
            Assert.That(Prices(document.Package.Rounds[1].Themes[0]), Is.EqualTo(new[] { 0, 0, 0 }));
        });

        document.OperationsManager.Undo.Execute(null);
        Assert.That(Prices(document.Package.Rounds[0].Themes[0]), Is.EqualTo(originalPrices));

        document.OperationsManager.Redo.Execute(null);
        Assert.That(Prices(document.Package.Rounds[1].Themes[0]), Is.EqualTo(new[] { 0, 0, 0 }));
    }

    [Test]
    public void MoveWithinFinalRound_ReordersThemeAndKeepsCanonicalZeroPrices()
    {
        using var document = CreateDocumentWithRoundType(
            RoundTypes.Final,
            ("First", [0]),
            ("Moved", [17, Question.InvalidPrice, 49]),
            ("Last", [0]));
        var round = document.Package.Rounds[0];
        var movedTheme = round.Themes[1];

        var result = document.ThemeMoves.Apply(
            document.ThemeMoves.CreateDragData(movedTheme),
            new ThemeLocation(0, 3),
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(ThemeMoveResult.Applied));
            Assert.That(round.Themes.Select(item => item.Model.Name), Is.EqualTo(new[] { "First", "Last", "Moved" }));
            Assert.That(Prices(movedTheme), Is.EqualTo(new[] { 0, 0, 0 }));
        });

        document.OperationsManager.Undo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(round.Themes.Select(item => item.Model.Name), Is.EqualTo(new[] { "First", "Moved", "Last" }));
            Assert.That(Prices(round.Themes[1]), Is.EqualTo(new[] { 17, Question.InvalidPrice, 49 }));
        });
    }

    [Test]
    public void InvalidStaleAndCancelledMoves_DoNotMutateDocumentOrCreateUndoEntry()
    {
        using var document = CreateTwoRoundDocument([100, 200]);
        var theme = document.Package.Rounds[0].Themes[0];
        var staleData = document.ThemeMoves.CreateDragData(theme);
        theme.Questions[0].Model.TypeName = QuestionTypes.Stake;

        var staleResult = document.ThemeMoves.Apply(staleData, new ThemeLocation(1, 0), true);
        var invalidTargetResult = document.ThemeMoves.Apply(
            document.ThemeMoves.CreateDragData(theme),
            new ThemeLocation(1, 2),
            true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledResult = document.ThemeMoves.Apply(
            document.ThemeMoves.CreateDragData(theme),
            new ThemeLocation(1, 0),
            true,
            cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(staleResult, Is.EqualTo(ThemeMoveResult.InvalidSource));
            Assert.That(invalidTargetResult, Is.EqualTo(ThemeMoveResult.InvalidTarget));
            Assert.That(cancelledResult, Is.EqualTo(ThemeMoveResult.Cancelled));
            Assert.That(document.Package.Rounds[0].Themes.Single(), Is.SameAs(theme));
            Assert.That(document.Package.Rounds[1].Themes, Is.Empty);
            Assert.That(document.OperationsManager.Undo.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void DragDataFromAnotherDocument_IsRejectedWithoutMutation()
    {
        using var source = CreateTwoRoundDocument([100]);
        using var target = CreateTwoRoundDocument([200]);
        var dragData = source.ThemeMoves.CreateDragData(source.Package.Rounds[0].Themes[0]);

        var result = target.ThemeMoves.Apply(dragData, new ThemeLocation(1, 0), true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(ThemeMoveResult.InvalidSource));
            Assert.That(target.Package.Rounds[0].Themes, Has.Count.EqualTo(1));
            Assert.That(target.Package.Rounds[1].Themes, Is.Empty);
            Assert.That(target.OperationsManager.Undo.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void DragPayload_RoundTripsWithExplicitVersionAndRejectsMalformedData()
    {
        var source = new ThemeDragData(
            "0123456789abcdef0123456789abcdef",
            new ThemeLocation(1, 2),
            "FINGERPRINT");

        var serialized = ThemeDragDataSerializer.Serialize(source);
        var restored = ThemeDragDataSerializer.TryDeserialize(serialized);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.EqualTo(source));
            Assert.That(serialized, Does.Contain($"\"Version\":{ThemeDragDataSerializer.CurrentVersion}"));
            Assert.That(ThemeDragDataSerializer.TryDeserialize("{not json"), Is.Null);
            Assert.That(ThemeDragDataSerializer.TryDeserialize("{\"Version\":2,\"Data\":null}"), Is.Null);
        });
    }

    private QDocument CreateDocument(params (string Name, int[] Prices)[] themes)
        => CreateDocumentWithRoundType(RoundTypes.Standart, themes);

    private QDocument CreateDocumentWithRoundType(
        string roundType,
        params (string Name, int[] Prices)[] themes)
    {
        var package = SIDocument.Create("Theme operations", "Test author");
        var round = new Round { Name = "Round", Type = roundType };

        foreach (var (name, prices) in themes)
        {
            var theme = new Theme { Name = name };

            foreach (var price in prices)
            {
                theme.Questions.Add(CreateQuestion($"Answer {price}", price));
            }

            round.Themes.Add(theme);
        }

        package.Package.Rounds.Add(round);
        return _documentFactory.CreateViewModelFor(package, "Theme operations");
    }

    private QDocument CreateTwoRoundDocument(int[] prices, string targetRoundType = RoundTypes.Standart)
    {
        var package = SIDocument.Create("Theme operations", "Test author");
        var sourceRound = new Round { Name = "Source" };
        var theme = new Theme { Name = "Moved" };

        foreach (var price in prices)
        {
            theme.Questions.Add(CreateQuestion($"Answer {price}", price));
        }

        sourceRound.Themes.Add(theme);
        package.Package.Rounds.Add(sourceRound);
        package.Package.Rounds.Add(new Round { Name = "Target", Type = targetRoundType });
        return _documentFactory.CreateViewModelFor(package, "Theme operations");
    }

    private static Theme CreateRichTheme(string imageName)
    {
        var theme = new Theme { Name = "Тема с медиа" };
        theme.Info.Authors.Add("author-1");
        theme.Info.Sources.Add("source-1");
        theme.Info.Comments.Text = "Комментарий темы";
        theme.Info.ShowmanComments = new Comments { Text = "Ведущему" };

        var mediaQuestion = CreateQuestion("Ответ", 17);
        mediaQuestion.TypeName = QuestionTypes.SecretPublicPrice;
        mediaQuestion.Parameters["customFutureParameter"] = new StepParameter
        {
            Type = StepParameterTypes.Simple,
            SimpleValue = "preserve-me",
        };
        mediaQuestion.Script = new Script();
        mediaQuestion.Script.Steps.Add(new Step
        {
            Type = StepTypes.ShowContent,
            Parameters =
            {
                [StepParameterNames.Content] = new StepParameter
                {
                    Type = StepParameterTypes.Content,
                    ContentValue =
                    [
                        new ContentItem { Type = ContentTypes.Text, Value = "Текст вопроса" },
                        new ContentItem { Type = ContentTypes.Image, Value = imageName, IsRef = true },
                    ],
                },
            },
        });
        mediaQuestion.Wrong.Add("Неверный ответ");
        mediaQuestion.Info.Comments.Text = "Комментарий вопроса";
        theme.Questions.Add(mediaQuestion);
        theme.Questions.Add(CreateQuestion("Второй ответ", Question.InvalidPrice));
        return theme;
    }

    private static Question CreateQuestion(string answer, int price)
    {
        var question = new Question { Price = price };
        question.Right.Add(answer);
        return question;
    }

    private static int[] Prices(ThemeViewModel theme) =>
        theme.Questions.Select(question => question.Model.Price).ToArray();

    private static string Serialize(Theme theme)
    {
        var content = new StringBuilder();
        using var writer = XmlWriter.Create(content, new XmlWriterSettings { OmitXmlDeclaration = true });
        theme.WriteXml(writer);
        writer.Flush();
        return content.ToString();
    }
}
