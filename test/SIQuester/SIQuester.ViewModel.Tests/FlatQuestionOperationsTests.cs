using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Serializers;
using SIQuester.ViewModel.Services;
using SIQuester.ViewModel.Tests.Helpers;

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
}
