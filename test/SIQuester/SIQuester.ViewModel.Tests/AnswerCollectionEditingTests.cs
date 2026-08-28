using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class AnswerCollectionEditingTests
{
    [Test]
    public void SimpleAnswerCollections_EnforceRightAndWrongRemovalRules()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Answer rules");
        var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];

        question.Right.CurrentPosition = 0;
        Assert.That(question.Right.RemoveItem.CanExecute(null), Is.False);

        question.Wrong.AddItem.Execute("Wrong answer");
        Assert.That(question.Wrong.RemoveItem.CanExecute(null), Is.True);
        question.Wrong.RemoveItem.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(question.Right, Has.Count.EqualTo(1));
            Assert.That(question.Wrong, Is.Empty);
            Assert.That(question.UsesSimpleAnswerCollections, Is.True);
        });
    }

    [Test]
    public void UsesSimpleAnswerCollections_TracksAnswerTypeChanges()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Answer types");
        var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];

        Assert.That(question.UsesSimpleAnswerCollections, Is.True);

        question.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_ManagedByClient);
        Assert.That(question.UsesSimpleAnswerCollections, Is.False);

        question.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Text);
        Assert.That(question.UsesSimpleAnswerCollections, Is.True);
    }

    [Test]
    public async Task FullSimpleAnswerCollections_SaveAndReloadWithoutDroppingEntries()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Ответы 例");
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester answers {Guid.NewGuid():N} 例.siq");
        qDocument.Path = filePath;

        try
        {
            var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];
            question.Right.CurrentPosition = 0;
            question.Right.CurrentItemValue = "Первый правильный 例";
            question.Right.AddItem.Execute("Second right answer");
            question.Right.AddItem.Execute("Третий правильный");
            question.Right.MoveLeft.Execute(null);
            question.Wrong.AddItem.Execute("Первый неправильный");
            question.Wrong.AddItem.Execute("Second wrong answer 例");

            await qDocument.Save.ExecuteAsync(null);

            await using var stream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(stream);
            var reloadedQuestion = reloaded.Package.Rounds[0].Themes[0].Questions[0];

            Assert.Multiple(() =>
            {
                Assert.That(reloadedQuestion.Right, Is.EqualTo(new[]
                {
                    "Первый правильный 例",
                    "Третий правильный",
                    "Second right answer",
                }));
                Assert.That(reloadedQuestion.Wrong, Is.EqualTo(new[]
                {
                    "Первый неправильный",
                    "Second wrong answer 例",
                }));
            });
        }
        finally
        {
            qDocument.Dispose();
            File.Delete(filePath);
        }
    }
}
