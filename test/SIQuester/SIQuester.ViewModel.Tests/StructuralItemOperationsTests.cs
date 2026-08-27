using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class StructuralItemOperationsTests
{
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
    public void StructuralMoveCommands_RejectCollectionBoundariesWithoutDirtyingDocument()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = CreatePackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Boundaries");
        var firstRound = document.Package.Rounds[0];
        var lastTheme = firstRound.Themes[^1];

        Assert.Multiple(() =>
        {
            Assert.That(firstRound.MoveEarlier.CanBeExecuted, Is.False);
            Assert.That(lastTheme.MoveLater.CanBeExecuted, Is.False);
        });

        firstRound.MoveEarlier.Execute(null);
        lastTheme.MoveLater.Execute(null);

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

        firstRound.Clone.Execute(null);

        AssertRoundOrder(document, "Round 1", "Round 2", "Round 3", "Round 1");
        document.OperationsManager.Undo.Execute(null);
        AssertRoundOrder(document, "Round 1", "Round 2", "Round 3");

        firstTheme.Clone.Execute(null);

        AssertThemeOrder(document.Package.Rounds[1], "Theme 2.1", "Theme 2.2", "Theme 2.3", "Theme 2.1");
        document.OperationsManager.Undo.Execute(null);
        AssertThemeOrder(document.Package.Rounds[1], "Theme 2.1", "Theme 2.2", "Theme 2.3");
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
