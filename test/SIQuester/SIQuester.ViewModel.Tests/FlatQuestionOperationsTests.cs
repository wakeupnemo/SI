using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Serializers;
using SIQuester.ViewModel.Services;
using SIQuester.ViewModel.Tests.Helpers;
using System.Text;
using System.Xml;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class FlatQuestionOperationsTests
{
    private IServiceProvider _serviceProvider = null!;
    private IDocumentViewModelFactory _documentFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _serviceProvider = TestHelper.CreateServiceProvider();
        _documentFactory = _serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
    }

    [TearDown]
    public void TearDown()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [Test]
    public void MoveWithinTheme_ReordersCanonicalDataAndKeepsPositionalPricesUndoable()
    {
        using var document = CreateDocument(
            ("Source", [("A", 100), ("B", 200), ("C", 300)]));
        var theme = document.Package.Rounds[0].Themes[0];
        var movedQuestion = theme.Questions[1];
        var dragData = document.FlatQuestions.CreateDragData(movedQuestion);

        var result = document.FlatQuestions.Apply(
            dragData,
            new FlatQuestionLocation(0, 0, 3),
            FlatQuestionDropMode.Move,
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FlatQuestionDropResult.Applied));
            Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "C", "B" }));
            Assert.That(theme.Model.Questions.Select(question => question.Right[0]),
                Is.EqualTo(new[] { "A", "C", "B" }));
            Assert.That(Prices(theme), Is.EqualTo(new[] { 100, 200, 300 }));
            Assert.That(theme.Questions[2], Is.SameAs(movedQuestion));
            Assert.That(document.ActiveNode, Is.SameAs(movedQuestion));
            Assert.That(document.OperationsManager.Undo.CanExecute(null), Is.True);
        });

        document.OperationsManager.Undo.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(Prices(theme), Is.EqualTo(new[] { 100, 200, 300 }));
        });

        document.OperationsManager.Redo.Execute(null);
        Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "C", "B" }));
    }

    [Test]
    public void MoveAcrossThemes_ClonesBeforeRemovalAndRecalculatesBothPriceSequences()
    {
        using var document = CreateDocument(
            ("Source", [("A", 100), ("B", 200), ("C", 300)]),
            ("Target", [("X", 400), ("Y", 800)]));
        var source = document.Package.Rounds[0].Themes[0];
        var target = document.Package.Rounds[0].Themes[1];
        var originalModel = source.Questions[0].Model;
        var dragData = document.FlatQuestions.CreateDragData(source.Questions[0]);

        var result = document.FlatQuestions.Apply(
            dragData,
            new FlatQuestionLocation(0, 1, 1),
            FlatQuestionDropMode.Move,
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FlatQuestionDropResult.Applied));
            Assert.That(Answers(source), Is.EqualTo(new[] { "B", "C" }));
            Assert.That(Prices(source), Is.EqualTo(new[] { 100, 200 }));
            Assert.That(Answers(target), Is.EqualTo(new[] { "X", "A", "Y" }));
            Assert.That(Prices(target), Is.EqualTo(new[] { 400, 800, 1200 }));
            Assert.That(target.Questions[1].Model, Is.Not.SameAs(originalModel));
            Assert.That(target.Model.Questions[1], Is.SameAs(target.Questions[1].Model));
            Assert.That(document.QuestionCount, Is.EqualTo(5));
        });

        document.OperationsManager.Undo.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(Answers(source), Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(Answers(target), Is.EqualTo(new[] { "X", "Y" }));
            Assert.That(Prices(source), Is.EqualTo(new[] { 100, 200, 300 }));
            Assert.That(Prices(target), Is.EqualTo(new[] { 400, 800 }));
            Assert.That(document.QuestionCount, Is.EqualTo(5));
        });
    }

    [Test]
    public void MoveAcrossRounds_PreservesQuestionSemanticsAndUsesBothThemesPriceSequences()
    {
        var package = SIDocument.Create("Cross-round question", "Test author");
        var sourceRound = new Round { Name = "Source round" };
        var sourceTheme = new Theme { Name = "Source" };
        sourceTheme.Questions.Add(CreateRichQuestion("Moved", 100));
        sourceTheme.Questions.Add(CreateRichQuestion("Remaining", 200));
        sourceRound.Themes.Add(sourceTheme);
        var targetRound = new Round { Name = "Target round" };
        var targetTheme = new Theme { Name = "Target" };
        targetTheme.Questions.Add(CreateRichQuestion("Target A", 400));
        targetTheme.Questions.Add(CreateRichQuestion("Target B", 800));
        targetRound.Themes.Add(targetTheme);
        package.Package.Rounds.Add(sourceRound);
        package.Package.Rounds.Add(targetRound);
        using var document = _documentFactory.CreateViewModelFor(package, "Cross-round question");
        var source = document.Package.Rounds[0].Themes[0];
        var target = document.Package.Rounds[1].Themes[0];
        var originalQuestion = source.Questions[0];
        document.Package.IsSelected = false;
        originalQuestion.IsSelected = true;
        document.ActiveNode = originalQuestion;
        var expectedQuestion = originalQuestion.Model.Clone();
        expectedQuestion.Price = 800;
        var dragData = document.FlatQuestions.CreateDragData(originalQuestion);

        var result = document.FlatQuestions.Apply(
            dragData,
            new FlatQuestionLocation(1, 0, 1),
            FlatQuestionDropMode.Move,
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FlatQuestionDropResult.Applied));
            Assert.That(Answers(source), Is.EqualTo(new[] { "Remaining" }));
            Assert.That(Prices(source), Is.EqualTo(new[] { 100 }));
            Assert.That(Answers(target), Is.EqualTo(new[] { "Target A", "Moved", "Target B" }));
            Assert.That(Prices(target), Is.EqualTo(new[] { 400, 800, 1200 }));
            Assert.That(Serialize(target.Questions[1].Model), Is.EqualTo(Serialize(expectedQuestion)));
            Assert.That(target.Questions[1].Model, Is.Not.SameAs(originalQuestion.Model));
            Assert.That(target.Model.Questions[1], Is.SameAs(target.Questions[1].Model));
        });

        document.OperationsManager.Undo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(Answers(source), Is.EqualTo(new[] { "Moved", "Remaining" }));
            Assert.That(Prices(source), Is.EqualTo(new[] { 100, 200 }));
            Assert.That(Answers(target), Is.EqualTo(new[] { "Target A", "Target B" }));
            Assert.That(Prices(target), Is.EqualTo(new[] { 400, 800 }));
            Assert.That(document.ActiveNode, Is.SameAs(originalQuestion));
            Assert.That(originalQuestion.IsSelected, Is.True);
            Assert.That(document.ActiveChain[^1], Is.SameAs(originalQuestion));
            Assert.That(document.ActiveChain, Has.Length.EqualTo(4));
        });

        document.OperationsManager.Redo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(document.ActiveNode, Is.SameAs(target.Questions[1]));
            Assert.That(target.Questions[1].IsSelected, Is.True);
            Assert.That(document.ActiveChain[^1], Is.SameAs(target.Questions[1]));
            Assert.That(document.ActiveChain, Has.Length.EqualTo(4));
        });
    }

    [Test]
    public void CopyWithinTheme_DuplicatesQuestionAndLeavesSourceAttached()
    {
        using var document = CreateDocument(
            ("Theme", [("A", 100), ("B", 200)]));
        var theme = document.Package.Rounds[0].Themes[0];
        var sourceQuestion = theme.Questions[0];
        var dragData = document.FlatQuestions.CreateDragData(sourceQuestion);

        var result = document.FlatQuestions.Apply(
            dragData,
            new FlatQuestionLocation(0, 0, 1),
            FlatQuestionDropMode.Copy,
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FlatQuestionDropResult.Applied));
            Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "A", "B" }));
            Assert.That(Prices(theme), Is.EqualTo(new[] { 100, 200, 300 }));
            Assert.That(theme.Questions[0], Is.SameAs(sourceQuestion));
            Assert.That(theme.Questions[1].Model, Is.Not.SameAs(sourceQuestion.Model));
            Assert.That(sourceQuestion.OwnerTheme, Is.SameAs(theme));
        });
    }

    [Test]
    public void MoveWithinTheme_WithoutRecalculationKeepsPriceWithQuestion()
    {
        using var document = CreateDocument(
            ("Theme", [("A", 100), ("B", 950), ("C", 300)]));
        var theme = document.Package.Rounds[0].Themes[0];
        var dragData = document.FlatQuestions.CreateDragData(theme.Questions[1]);

        var result = document.FlatQuestions.Apply(
            dragData,
            new FlatQuestionLocation(0, 0, 3),
            FlatQuestionDropMode.Move,
            recalculatePrices: false);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FlatQuestionDropResult.Applied));
            Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "C", "B" }));
            Assert.That(Prices(theme), Is.EqualTo(new[] { 100, 300, 950 }));
        });
    }

    [Test]
    public void KeyboardCommands_MoveInBothDirectionsAndDuplicateAsUndoableCanonicalChanges()
    {
        using var document = CreateDocument(
            ("Theme", [("A", 100), ("B", 200), ("C", 300)]));
        var theme = document.Package.Rounds[0].Themes[0];
        var question = theme.Questions[1];

        document.MoveFlatQuestionBackward.Execute(question);
        Assert.That(Answers(theme), Is.EqualTo(new[] { "B", "A", "C" }));

        document.MoveFlatQuestionForward.Execute(question);
        Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "B", "C" }));

        document.DuplicateFlatQuestion.Execute(question);

        Assert.Multiple(() =>
        {
            Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "B", "B", "C" }));
            Assert.That(Prices(theme), Is.EqualTo(new[] { 100, 200, 300, 400 }));
            Assert.That(theme.Questions[2].Model, Is.Not.SameAs(question.Model));
            Assert.That(document.ActiveNode, Is.SameAs(theme.Questions[2]));
        });

        document.OperationsManager.Undo.Execute(null);
        Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void MoveWithinTheme_AtBoundaryIsNoChangeAndInvalidOffsetIsRejected()
    {
        using var document = CreateDocument(
            ("Theme", [("A", 100), ("B", 200)]));
        var theme = document.Package.Rounds[0].Themes[0];

        var backward = document.FlatQuestions.MoveWithinTheme(
            theme.Questions[0],
            -1,
            recalculatePrices: true);
        var forward = document.FlatQuestions.MoveWithinTheme(
            theme.Questions[1],
            1,
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(backward, Is.EqualTo(FlatQuestionDropResult.NoChange));
            Assert.That(forward, Is.EqualTo(FlatQuestionDropResult.NoChange));
            Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(document.OperationsManager.Undo.CanExecute(null), Is.False);
            Assert.That(
                () => document.FlatQuestions.MoveWithinTheme(theme.Questions[0], 2, true),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void MoveWithRecalculation_DoesNotPropagateInvalidPriceIntoOrdinaryQuestions()
    {
        using var document = CreateDocument(
            ("Theme", [("Special", Question.InvalidPrice), ("A", 100), ("B", 200)]));
        var theme = document.Package.Rounds[0].Themes[0];
        var dragData = document.FlatQuestions.CreateDragData(theme.Questions[2]);

        var result = document.FlatQuestions.Apply(
            dragData,
            new FlatQuestionLocation(0, 0, 1),
            FlatQuestionDropMode.Move,
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FlatQuestionDropResult.Applied));
            Assert.That(Answers(theme), Is.EqualTo(new[] { "Special", "B", "A" }));
            Assert.That(Prices(theme), Is.EqualTo(new[] { Question.InvalidPrice, 100, 200 }));
        });
    }

    [Test]
    public void CancelledDrop_DoesNotMutateDocumentOrCreateUndoEntry()
    {
        using var document = CreateDocument(
            ("Theme", [("A", 100), ("B", 200)]));
        var theme = document.Package.Rounds[0].Themes[0];
        var dragData = document.FlatQuestions.CreateDragData(theme.Questions[0]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = document.FlatQuestions.Apply(
            dragData,
            new FlatQuestionLocation(0, 0, 2),
            FlatQuestionDropMode.Move,
            recalculatePrices: true,
            cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FlatQuestionDropResult.Cancelled));
            Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(document.OperationsManager.Undo.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void StaleDragData_IsRejectedWithoutMutatingTheReplacementQuestion()
    {
        using var document = CreateDocument(
            ("Theme", [("A", 100), ("B", 200)]));
        var theme = document.Package.Rounds[0].Themes[0];
        var dragData = document.FlatQuestions.CreateDragData(theme.Questions[0]);
        theme.Questions[0].Model.Right[0] = "Changed while dragging";

        var result = document.FlatQuestions.Apply(
            dragData,
            new FlatQuestionLocation(0, 0, 2),
            FlatQuestionDropMode.Move,
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FlatQuestionDropResult.InvalidSource));
            Assert.That(Answers(theme), Is.EqualTo(new[] { "Changed while dragging", "B" }));
            Assert.That(theme.Questions, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void InvalidTarget_IsRejectedWithoutMutation()
    {
        using var document = CreateDocument(
            ("Theme", [("A", 100), ("B", 200)]));
        var theme = document.Package.Rounds[0].Themes[0];
        var dragData = document.FlatQuestions.CreateDragData(theme.Questions[0]);

        var result = document.FlatQuestions.Apply(
            dragData,
            new FlatQuestionLocation(0, 0, 3),
            FlatQuestionDropMode.Move,
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FlatQuestionDropResult.InvalidTarget));
            Assert.That(Answers(theme), Is.EqualTo(new[] { "A", "B" }));
        });
    }

    [Test]
    public void DragDataFromAnotherDocument_IsRejectedWithoutMutation()
    {
        using var sourceDocument = CreateDocument(("Source", [("A", 100)]));
        using var targetDocument = CreateDocument(("Target", [("B", 200)]));
        var dragData = sourceDocument.FlatQuestions.CreateDragData(
            sourceDocument.Package.Rounds[0].Themes[0].Questions[0]);
        var targetTheme = targetDocument.Package.Rounds[0].Themes[0];

        var result = targetDocument.FlatQuestions.Apply(
            dragData,
            new FlatQuestionLocation(0, 0, 1),
            FlatQuestionDropMode.Copy,
            recalculatePrices: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FlatQuestionDropResult.InvalidSource));
            Assert.That(Answers(targetTheme), Is.EqualTo(new[] { "B" }));
            Assert.That(targetDocument.OperationsManager.Undo.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void DragPayload_RoundTripsWithExplicitVersionAndRejectsMalformedData()
    {
        var source = new FlatQuestionDragData(
            "0123456789abcdef0123456789abcdef",
            new FlatQuestionLocation(1, 2, 3),
            "FINGERPRINT");

        var serialized = FlatQuestionDragDataSerializer.Serialize(source);
        var restored = FlatQuestionDragDataSerializer.TryDeserialize(serialized);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.EqualTo(source));
            Assert.That(serialized, Does.Contain($"\"Version\":{FlatQuestionDragDataSerializer.CurrentVersion}"));
            Assert.That(FlatQuestionDragDataSerializer.TryDeserialize("{not json"), Is.Null);
            Assert.That(FlatQuestionDragDataSerializer.TryDeserialize("{\"Version\":2,\"Data\":null}"), Is.Null);
        });
    }

    [Test]
    public void FlatDetailRows_TrackThemeStructureAndRetainEmptyRounds()
    {
        using var document = CreateDocument(
            ("Theme A", new[] { ("A", 100) }),
            ("Theme B", new[] { ("B", 200) }));
        var firstRound = document.Package.Rounds[0];

        Assert.Multiple(() =>
        {
            Assert.That(document.FlatDetailRows, Has.Count.EqualTo(2));
            Assert.That(document.FlatDetailRows[0].StartsRound, Is.True);
            Assert.That(document.FlatDetailRows[1].StartsRound, Is.False);
        });

        using (var change = document.OperationsManager.BeginComplexChange())
        {
            firstRound.Themes.RemoveAt(0);
            change.Commit();
        }

        Assert.Multiple(() =>
        {
            Assert.That(document.FlatDetailRows, Has.Count.EqualTo(1));
            Assert.That(document.FlatDetailRows[0].Theme, Is.SameAs(firstRound.Themes[0]));
            Assert.That(document.FlatDetailRows[0].StartsRound, Is.True);
        });

        document.OperationsManager.Undo.Execute(null);
        var emptyRound = new RoundViewModel(new Round { Name = "Empty" });
        document.Package.Rounds.Add(emptyRound);

        Assert.Multiple(() =>
        {
            Assert.That(document.FlatDetailRows, Has.Count.EqualTo(3));
            Assert.That(document.FlatDetailRows[^1].Round, Is.SameAs(emptyRound));
            Assert.That(document.FlatDetailRows[^1].Theme, Is.Null);
            Assert.That(document.FlatDetailRows[^1].StartsRound, Is.True);
        });
    }

    private QDocument CreateDocument(params (string Theme, (string Answer, int Price)[] Questions)[] themes)
    {
        var package = SIDocument.Create("Flat operations", "Test author");
        var round = new Round { Name = "Round" };

        foreach (var themeData in themes)
        {
            var theme = new Theme { Name = themeData.Theme };

            foreach (var questionData in themeData.Questions)
            {
                var question = new Question { Price = questionData.Price };
                question.Right.Add(questionData.Answer);
                theme.Questions.Add(question);
            }

            round.Themes.Add(theme);
        }

        package.Package.Rounds.Add(round);
        return _documentFactory.CreateViewModelFor(package, "Flat operations");
    }

    private static string[] Answers(ThemeViewModel theme) =>
        theme.Questions.Select(question => question.Model.Right[0]).ToArray();

    private static int[] Prices(ThemeViewModel theme) =>
        theme.Questions.Select(question => question.Model.Price).ToArray();

    private static Question CreateRichQuestion(string answer, int price)
    {
        var question = new Question
        {
            Price = price,
            TypeName = QuestionTypes.Stake,
            Script = new Script(),
        };
        question.Right.Add(answer);
        question.Wrong.Add($"Wrong {answer}");
        question.Info.Comments.Text = $"Comment {answer}";
        question.Parameters["futureParameter"] = new StepParameter
        {
            Type = StepParameterTypes.Simple,
            SimpleValue = "future-value",
        };
        question.Script.Steps.Add(new Step
        {
            Type = StepTypes.ShowContent,
            Parameters =
            {
                [StepParameterNames.Content] = new StepParameter
                {
                    Type = StepParameterTypes.Content,
                    ContentValue = [new ContentItem { Type = ContentTypes.Text, Value = $"Text {answer}" }],
                },
            },
        });
        return question;
    }

    private static string Serialize(Question question)
    {
        var content = new StringBuilder();
        using var writer = XmlWriter.Create(content, new XmlWriterSettings { OmitXmlDeclaration = true });
        question.WriteXml(writer);
        writer.Flush();
        return content.ToString();
    }
}
