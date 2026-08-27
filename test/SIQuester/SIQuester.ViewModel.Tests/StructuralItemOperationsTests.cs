using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class StructuralItemOperationsTests
{
    [Test]
    public void HierarchyCreateAndDelete_UpdateCanonicalCollectionsAndUndoAsSingleChanges()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = SIDocument.Create("Hierarchy operations", "Author");
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Hierarchy operations");
        var previousCreateQuestionsWithTheme = document.Settings.CreateQuestionsWithTheme;
        document.Settings.CreateQuestionsWithTheme = false;

        try
        {
            document.Package.AddRound.Execute(null);
            var round = document.Package.Rounds.Single();
            Assert.That(document.Document.Package.Rounds.Single(), Is.SameAs(round.Model));
            document.OperationsManager.Undo.Execute(null);
            Assert.That(document.Package.Rounds, Is.Empty);
            document.OperationsManager.Redo.Execute(null);
            round = document.Package.Rounds.Single();

            round.AddTheme.Execute(null);
            var theme = round.Themes.Single();
            Assert.That(round.Model.Themes.Single(), Is.SameAs(theme.Model));
            document.OperationsManager.Undo.Execute(null);
            Assert.That(round.Themes, Is.Empty);
            document.OperationsManager.Redo.Execute(null);
            theme = round.Themes.Single();

            theme.AddQuestion.Execute(null);
            var question = theme.Questions.Single();
            Assert.That(theme.Model.Questions.Single(), Is.SameAs(question.Model));
            document.OperationsManager.Undo.Execute(null);
            Assert.That(theme.Questions, Is.Empty);
            document.OperationsManager.Redo.Execute(null);
            question = theme.Questions.Single();

            question.Remove!.Execute(null);
            Assert.That(theme.Questions, Is.Empty);
            document.OperationsManager.Undo.Execute(null);
            Assert.That(theme.Questions.Single(), Is.SameAs(question));

            theme.Remove!.Execute(null);
            Assert.That(round.Themes, Is.Empty);
            document.OperationsManager.Undo.Execute(null);
            Assert.That(round.Themes.Single(), Is.SameAs(theme));

            round.Remove!.Execute(null);
            Assert.That(document.Package.Rounds, Is.Empty);
            document.OperationsManager.Undo.Execute(null);
            Assert.That(document.Package.Rounds.Single(), Is.SameAs(round));
        }
        finally
        {
            document.Settings.CreateQuestionsWithTheme = previousCreateQuestionsWithTheme;
        }
    }

    [Test]
    public void RoundMoveAndDuplicate_KeepCanonicalOrderAndUndoRedoAsSingleChanges()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = CreatePackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Round operations");
        var movedRound = document.Package.Rounds[2];

        movedRound.MoveEarlier.Execute(null);

        AssertRoundOrder(document, "Round 1", "Round 3", "Round 2");
        Assert.Multiple(() =>
        {
            Assert.That(movedRound.MoveEarlier.CanBeExecuted, Is.True);
            Assert.That(movedRound.MoveLater.CanBeExecuted, Is.True);
        });

        document.OperationsManager.Undo.Execute(null);
        AssertRoundOrder(document, "Round 1", "Round 2", "Round 3");
        document.OperationsManager.Redo.Execute(null);
        AssertRoundOrder(document, "Round 1", "Round 3", "Round 2");

        movedRound.Duplicate.Execute(null);
        var clone = document.Package.Rounds[2];

        Assert.Multiple(() =>
        {
            AssertRoundOrder(document, "Round 1", "Round 3", "Round 3", "Round 2");
            Assert.That(clone, Is.Not.SameAs(movedRound));
            Assert.That(clone.Model, Is.Not.SameAs(movedRound.Model));
            Assert.That(clone.Themes[0].Model, Is.Not.SameAs(movedRound.Themes[0].Model));
            Assert.That(clone.Themes[0].Questions[0].Model, Is.Not.SameAs(movedRound.Themes[0].Questions[0].Model));
        });

        document.OperationsManager.Undo.Execute(null);
        AssertRoundOrder(document, "Round 1", "Round 3", "Round 2");
        document.OperationsManager.Redo.Execute(null);
        AssertRoundOrder(document, "Round 1", "Round 3", "Round 3", "Round 2");
    }

    [Test]
    public void ThemeMoveAndDuplicate_KeepCanonicalOrderAndUndoRedoAsSingleChanges()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = CreatePackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Theme operations");
        var round = document.Package.Rounds[0];
        var movedTheme = round.Themes[2];

        movedTheme.MoveEarlier.Execute(null);

        AssertThemeOrder(round, "Theme 1.1", "Theme 1.3", "Theme 1.2");
        document.OperationsManager.Undo.Execute(null);
        AssertThemeOrder(round, "Theme 1.1", "Theme 1.2", "Theme 1.3");
        document.OperationsManager.Redo.Execute(null);
        AssertThemeOrder(round, "Theme 1.1", "Theme 1.3", "Theme 1.2");

        movedTheme.Duplicate.Execute(null);
        var clone = round.Themes[2];

        Assert.Multiple(() =>
        {
            AssertThemeOrder(round, "Theme 1.1", "Theme 1.3", "Theme 1.3", "Theme 1.2");
            Assert.That(clone, Is.Not.SameAs(movedTheme));
            Assert.That(clone.Model, Is.Not.SameAs(movedTheme.Model));
            Assert.That(clone.Questions[0].Model, Is.Not.SameAs(movedTheme.Questions[0].Model));
        });

        document.OperationsManager.Undo.Execute(null);
        AssertThemeOrder(round, "Theme 1.1", "Theme 1.3", "Theme 1.2");
        document.OperationsManager.Redo.Execute(null);
        AssertThemeOrder(round, "Theme 1.1", "Theme 1.3", "Theme 1.3", "Theme 1.2");
    }

    [Test]
    public async Task QuestionMoveAndDuplicate_ReusePriceRulesAndRoundTripAsSingleChanges()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = CreatePackage();
        var modelTheme = package.Package.Rounds[0].Themes[0];
        modelTheme.Questions.Add(new Question { Price = 200, Right = { "Answer 2" } });
        modelTheme.Questions.Add(new Question { Price = 300, Right = { "Answer 3" } });
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Question operations");
        var theme = document.Package.Rounds[0].Themes[0];
        var first = theme.Questions[0];
        var second = theme.Questions[1];
        var moved = theme.Questions[2];
        var previousChangePriceOnMove = document.Settings.ChangePriceOnMove;
        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"SIQuester question operations {Guid.NewGuid():N} пример 例.siq");
        document.Settings.ChangePriceOnMove = true;
        document.Path = filePath;

        try
        {
            moved.MoveEarlier.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(theme.Questions, Is.EqualTo(new[] { first, moved, second }));
                Assert.That(theme.Model.Questions, Is.EqualTo(new[] { first.Model, moved.Model, second.Model }));
                Assert.That(theme.Questions.Select(question => question.Model.Price),
                    Is.EqualTo(new[] { 100, 200, 300 }),
                    "the shared move operation must keep prices attached to positions when configured");
                Assert.That(moved.MoveEarlier.CanBeExecuted, Is.True);
                Assert.That(moved.MoveLater.CanBeExecuted, Is.True);
            });

            document.OperationsManager.Undo.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(theme.Questions, Is.EqualTo(new[] { first, second, moved }));
                Assert.That(theme.Questions.Select(question => question.Model.Price),
                    Is.EqualTo(new[] { 100, 200, 300 }));
            });
            document.OperationsManager.Redo.Execute(null);

            moved.Duplicate.Execute(null);
            var clone = theme.Questions[2];

            Assert.Multiple(() =>
            {
                Assert.That(theme.Questions, Is.EqualTo(new[] { first, moved, clone, second }));
                Assert.That(clone, Is.Not.SameAs(moved));
                Assert.That(clone.Model, Is.Not.SameAs(moved.Model));
                Assert.That(clone.Right, Is.EqualTo(moved.Right));
                Assert.That(theme.Questions.Select(question => question.Model.Price),
                    Is.EqualTo(new[] { 100, 200, 300, 400 }));
            });

            document.OperationsManager.Undo.Execute(null);
            Assert.That(theme.Questions, Is.EqualTo(new[] { first, moved, second }));
            document.OperationsManager.Redo.Execute(null);
            Assert.That(theme.Questions, Has.Count.EqualTo(4));

            await document.Save.ExecuteAsync(null);
            await using var stream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(stream);
            var reloadedQuestions = reloaded.Package.Rounds[0].Themes[0].Questions;

            Assert.Multiple(() =>
            {
                Assert.That(reloadedQuestions.Select(question => question.Price),
                    Is.EqualTo(new[] { 100, 200, 300, 400 }));
                Assert.That(reloadedQuestions.Select(question => question.Right.Single()),
                    Is.EqualTo(new[] { "Answer 1.1", "Answer 3", "Answer 3", "Answer 2" }));
            });
        }
        finally
        {
            document.Settings.ChangePriceOnMove = previousChangePriceOnMove;

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    public void StructuralMoveCommands_RejectCollectionBoundariesWithoutDirtyingDocument()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = CreatePackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Boundaries");
        var firstRound = document.Package.Rounds[0];
        var lastTheme = firstRound.Themes[^1];
        var onlyQuestion = firstRound.Themes[0].Questions.Single();

        Assert.Multiple(() =>
        {
            Assert.That(firstRound.MoveEarlier.CanBeExecuted, Is.False);
            Assert.That(lastTheme.MoveLater.CanBeExecuted, Is.False);
            Assert.That(onlyQuestion.MoveEarlier.CanBeExecuted, Is.False);
            Assert.That(onlyQuestion.MoveLater.CanBeExecuted, Is.False);
        });

        firstRound.MoveEarlier.Execute(null);
        lastTheme.MoveLater.Execute(null);
        onlyQuestion.MoveEarlier.Execute(null);
        onlyQuestion.MoveLater.Execute(null);

        Assert.Multiple(() =>
        {
            AssertRoundOrder(document, "Round 1", "Round 2", "Round 3");
            AssertThemeOrder(firstRound, "Theme 1.1", "Theme 1.2", "Theme 1.3");
            Assert.That(document.OperationsManager.Undo.CanBeExecuted, Is.False);
        });
    }

    [Test]
    public async Task RoundAndThemeOperations_SaveAndReloadThroughCanonicalPackageFormat()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = CreatePackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Structural persistence");
        var movedRound = document.Package.Rounds[2];
        movedRound.MoveEarlier.Execute(null);
        movedRound.Duplicate.Execute(null);
        var clonedRound = document.Package.Rounds[2];
        var movedTheme = clonedRound.Themes[2];
        movedTheme.MoveEarlier.Execute(null);
        movedTheme.Duplicate.Execute(null);
        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"SIQuester structural operations {Guid.NewGuid():N} пример 例.siq");
        document.Path = filePath;

        try
        {
            await document.Save.ExecuteAsync(null);
            await using var stream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(stream);

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Package.Rounds.Select(round => round.Name),
                    Is.EqualTo(new[] { "Round 1", "Round 3", "Round 3", "Round 2" }));
                Assert.That(reloaded.Package.Rounds[2].Themes.Select(theme => theme.Name),
                    Is.EqualTo(new[] { "Theme 3.1", "Theme 3.3", "Theme 3.3", "Theme 3.2" }));
                Assert.That(reloaded.Package.Rounds[2].Themes[2].Questions.Single().Right,
                    Is.EqualTo(new[] { "Answer 3.3" }));
            });
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    public void LegacyCloneCommands_KeepAppendBehaviorForWpfCompatibility()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = CreatePackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Legacy clone compatibility");
        var firstRound = document.Package.Rounds[0];
        var firstTheme = document.Package.Rounds[1].Themes[0];
        var firstQuestion = document.Package.Rounds[2].Themes[0].Questions[0];

        firstRound.Clone.Execute(null);

        AssertRoundOrder(document, "Round 1", "Round 2", "Round 3", "Round 1");
        document.OperationsManager.Undo.Execute(null);
        AssertRoundOrder(document, "Round 1", "Round 2", "Round 3");

        firstTheme.Clone.Execute(null);

        AssertThemeOrder(document.Package.Rounds[1], "Theme 2.1", "Theme 2.2", "Theme 2.3", "Theme 2.1");
        document.OperationsManager.Undo.Execute(null);
        AssertThemeOrder(document.Package.Rounds[1], "Theme 2.1", "Theme 2.2", "Theme 2.3");

        firstQuestion.Clone.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(document.Package.Rounds[2].Themes[0].Questions, Has.Count.EqualTo(2));
            Assert.That(document.Package.Rounds[2].Themes[0].Questions[0], Is.SameAs(firstQuestion));
            Assert.That(document.Package.Rounds[2].Themes[0].Questions[1].Model,
                Is.Not.SameAs(firstQuestion.Model));
        });
        document.OperationsManager.Undo.Execute(null);
        Assert.That(document.Package.Rounds[2].Themes[0].Questions, Has.Count.EqualTo(1));
    }

    private static SIDocument CreatePackage()
    {
        var package = SIDocument.Create("Structure", "Author");

        for (var roundIndex = 1; roundIndex <= 3; roundIndex++)
        {
            var round = new Round { Name = $"Round {roundIndex}" };

            for (var themeIndex = 1; themeIndex <= 3; themeIndex++)
            {
                var theme = new Theme { Name = $"Theme {roundIndex}.{themeIndex}" };
                theme.Questions.Add(new Question { Price = 100, Right = { $"Answer {roundIndex}.{themeIndex}" } });
                round.Themes.Add(theme);
            }

            package.Package.Rounds.Add(round);
        }

        return package;
    }

    private static void AssertRoundOrder(QDocument document, params string[] names)
    {
        Assert.Multiple(() =>
        {
            Assert.That(document.Package.Rounds.Select(round => round.Model.Name), Is.EqualTo(names));
            Assert.That(document.Document.Package.Rounds.Select(round => round.Name), Is.EqualTo(names));
        });
    }

    private static void AssertThemeOrder(RoundViewModel round, params string[] names)
    {
        Assert.Multiple(() =>
        {
            Assert.That(round.Themes.Select(theme => theme.Model.Name), Is.EqualTo(names));
            Assert.That(round.Model.Themes.Select(theme => theme.Name), Is.EqualTo(names));
        });
    }
}
