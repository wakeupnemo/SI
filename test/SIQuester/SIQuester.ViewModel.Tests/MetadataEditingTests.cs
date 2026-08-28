using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class MetadataEditingTests
{
    [Test]
    public void CurrentItemValue_WithDuplicateEntries_EditsTheSelectedIndex()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Duplicate metadata");
        var tags = qDocument.Package.Tags;

        tags.AddItem.Execute("duplicate");
        tags.AddItem.Execute("duplicate");
        tags.CurrentPosition = 1;
        tags.CurrentItemValue = "selected duplicate";

        Assert.Multiple(() =>
        {
            Assert.That(tags, Is.EqualTo(new[] { "duplicate", "selected duplicate" }));
            Assert.That(tags.CurrentPosition, Is.EqualTo(1));
            Assert.That(tags.CurrentItemValue, Is.EqualTo("selected duplicate"));
            Assert.That(qDocument.Package.Model.Tags, Is.EqualTo(tags));
        });
    }

    [Test]
    public void MetadataCollectionCommands_AddMoveAndRemoveBySelectedIndex()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Metadata commands");
        var sources = qDocument.Package.Rounds[0].Themes[0].Questions[0].Info.Sources;

        sources.AddItem.Execute("first");
        sources.AddItem.Execute("second");
        sources.MoveLeft.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(sources, Is.EqualTo(new[] { "second", "first" }));
            Assert.That(sources.CurrentPosition, Is.EqualTo(0));
            Assert.That(sources.RemoveItem.CanExecute(null), Is.True);
        });

        sources.RemoveItem.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(sources, Is.EqualTo(new[] { "first" }));
            Assert.That(sources.CurrentPosition, Is.EqualTo(0));
            Assert.That(sources.CurrentItemValue, Is.EqualTo("first"));
        });
    }

    [Test]
    public void PackageAuthors_CannotDeleteTheLastAuthor()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Author invariant");
        var authors = qDocument.Package.Info.Authors;

        authors.CurrentPosition = 0;
        Assert.That(authors.RemoveItem.CanExecute(null), Is.False);

        authors.AddItem.Execute("second-author");
        Assert.That(authors.RemoveItem.CanExecute(null), Is.True);
        authors.RemoveItem.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(authors, Has.Count.EqualTo(1));
            Assert.That(authors.RemoveItem.CanExecute(null), Is.False);
        });
    }

    [Test]
    public async Task TypedMetadataEdits_SaveAndReloadAtEveryPackageLevel()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Метаданные 例");
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester metadata {Guid.NewGuid():N} 例.siq");
        qDocument.Path = filePath;

        try
        {
            var package = qDocument.Package;
            var round = package.Rounds[0];
            var theme = round.Themes[0];
            var question = theme.Questions[0];

            AddEntry(package.Tags, "тег 例");
            SetInfo(package.Info, "package-author", "package-source", "Комментарий пакета", "Ведущему пакета");
            SetInfo(round.Info, "round-author", "round-source", "Комментарий раунда", "Ведущему раунда");
            SetInfo(theme.Info, "theme-author", "theme-source", "Комментарий темы", "Ведущему темы");
            SetInfo(question.Info, "question-author", "question-source", "Комментарий вопроса", "Ведущему вопроса");

            await qDocument.Save.ExecuteAsync(null);

            await using var stream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(stream);
            var reloadedRound = reloaded.Package.Rounds[0];
            var reloadedTheme = reloadedRound.Themes[0];
            var reloadedQuestion = reloadedTheme.Questions[0];

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Package.Tags, Does.Contain("тег 例"));
                AssertInfo(reloaded.Package.Info, "package-author", "package-source", "Комментарий пакета", "Ведущему пакета");
                AssertInfo(reloadedRound.Info, "round-author", "round-source", "Комментарий раунда", "Ведущему раунда");
                AssertInfo(reloadedTheme.Info, "theme-author", "theme-source", "Комментарий темы", "Ведущему темы");
                AssertInfo(reloadedQuestion.Info, "question-author", "question-source", "Комментарий вопроса", "Ведущему вопроса");
            });
        }
        finally
        {
            qDocument.Dispose();
            File.Delete(filePath);
        }
    }

    [Test]
    public async Task TypedPackageAndRoundFields_SaveAndReloadWithoutNormalizingFutureValues()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Поля пакета 例");
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester package fields {Guid.NewGuid():N} 例.siq");
        qDocument.Path = filePath;

        try
        {
            var package = qDocument.Package.Model;
            package.Publisher = "Издатель 例";
            Assert.That(qDocument.OperationsManager.Undo.CanExecute(null), Is.True);
            qDocument.OperationsManager.Undo.Execute(null);
            Assert.That(package.Publisher, Is.Empty);
            qDocument.OperationsManager.Redo.Execute(null);
            Assert.That(package.Publisher, Is.EqualTo("Издатель 例"));
            package.ContactUri = "mailto:автор@example.test";
            package.Date = "осень 2026 例";
            package.Language = "x-future-例";
            package.Restriction = "16+ — регион 例";
            package.Difficulty = 10;
            var round = qDocument.Package.Rounds[0];
            round.SetType.Execute(RoundTypes.Final);
            Assert.That(round.Model.Type, Is.EqualTo(RoundTypes.Final));
            qDocument.OperationsManager.Undo.Execute(null);
            Assert.That(round.Model.Type, Is.EqualTo(RoundTypes.Standart));
            qDocument.OperationsManager.Redo.Execute(null);
            Assert.That(round.Model.Type, Is.EqualTo(RoundTypes.Final));
            round.Model.Type = "future-round-例";

            await qDocument.Save.ExecuteAsync(null);

            await using var stream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(stream);

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Package.Publisher, Is.EqualTo("Издатель 例"));
                Assert.That(reloaded.Package.ContactUri, Is.EqualTo("mailto:автор@example.test"));
                Assert.That(reloaded.Package.Date, Is.EqualTo("осень 2026 例"));
                Assert.That(reloaded.Package.Language, Is.EqualTo("x-future-例"));
                Assert.That(reloaded.Package.Restriction, Is.EqualTo("16+ — регион 例"));
                Assert.That(reloaded.Package.Difficulty, Is.EqualTo(10));
                Assert.That(reloaded.Package.Rounds[0].Type, Is.EqualTo("future-round-例"));
            });
        }
        finally
        {
            qDocument.Dispose();
            File.Delete(filePath);
        }
    }

    private static void SetInfo(
        InfoViewModel info,
        string author,
        string source,
        string comments,
        string showmanComments)
    {
        AddEntry(info.Authors, author);
        AddEntry(info.Sources, source);
        info.Comments.Text = comments;
        info.ShowmanComments.Text = showmanComments;
    }

    private static void AddEntry(ItemsViewModel<string> items, string value)
    {
        items.AddItem.Execute(string.Empty);
        items.CurrentItemValue = value;
    }

    private static void AssertInfo(
        Info info,
        string author,
        string source,
        string comments,
        string showmanComments)
    {
        Assert.That(info.Authors, Does.Contain(author));
        Assert.That(info.Sources, Does.Contain(source));
        Assert.That(info.Comments.Text, Is.EqualTo(comments));
        Assert.That(info.ShowmanComments?.Text, Is.EqualTo(showmanComments));
    }
}
