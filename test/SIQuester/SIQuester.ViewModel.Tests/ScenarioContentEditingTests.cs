using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class ScenarioContentEditingTests
{
    [Test]
    public void LegacyQuestion_ExposesCanonicalContentWithoutCreatingScript()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = SIDocument.Create("Legacy content", "Author");
        var round = new Round { Name = "Round" };
        var theme = new Theme { Name = "Theme" };
        var question = new Question { Price = 100 };
        question.Parameters[QuestionParameterNames.Question] = new StepParameter
        {
            Type = StepParameterTypes.Content,
            ContentValue = new List<ContentItem>
            {
                new() { Type = ContentTypes.Text, Value = "Legacy text" },
                new() { Type = ContentTypes.Audio, Value = "voice.ogg", IsRef = true },
            },
        };
        question.Right.Add("Answer");
        theme.Questions.Add(question);
        round.Themes.Add(theme);
        document.Package.Rounds.Add(round);
        using var qDocument = factory.CreateViewModelFor(document, "Legacy content");
        var questionViewModel = qDocument.Package.Rounds[0].Themes[0].Questions[0];

        questionViewModel.LegacyContent!.CurrentPosition = 0;
        questionViewModel.LegacyContent.CurrentItem!.Model.Value = "Edited legacy text";

        Assert.Multiple(() =>
        {
            Assert.That(questionViewModel.HasScript, Is.False);
            Assert.That(questionViewModel.ScriptSteps, Is.Empty);
            Assert.That(questionViewModel.LegacyContent, Has.Count.EqualTo(2));
            Assert.That(question.Script, Is.Null);
            Assert.That(question.Parameters[QuestionParameterNames.Question].ContentValue![0].Value,
                Is.EqualTo("Edited legacy text"));
            Assert.That(qDocument.OperationsManager.Undo.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task ScriptSteps_AllParameterKindsAndContentAttributes_SaveAndReload()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = CreateScriptPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Сценарий 例");
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester scenario {Guid.NewGuid():N} 例.siq");
        qDocument.Path = filePath;

        try
        {
            var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];
            var showStep = question.ScriptSteps[0];
            var content = showStep.Parameters.Single(parameter => parameter.Key == StepParameterNames.Content)
                .Value.ContentValue!;
            var originalType = showStep.Model.Type;

            showStep.Model.Type = "showContentCustom";
            qDocument.OperationsManager.Undo.Execute(null);
            Assert.That(showStep.Model.Type, Is.EqualTo(originalType));
            qDocument.OperationsManager.Redo.Execute(null);

            content.CurrentPosition = 0;
            content.CurrentItem!.Model.Value = "Текст сценария 例";
            content.CurrentItem.DurationSeconds = 7.5m;
            content.CurrentItem.Model.WaitForFinish = false;
            content.AddVoice.Execute(null);
            content.CurrentItem!.Model.Value = "Реплика ведущего";

            var simple = question.ScriptSteps[1].Parameters.Single().Value;
            simple.Model.SimpleValue = "direct-custom";

            var complex = question.ScriptSteps[2].Parameters;
            complex.Single(parameter => parameter.Key == "range").Value.NumberSetValue!.Minimum = 10;
            complex.Single(parameter => parameter.Key == "range").Value.NumberSetValue!.Maximum = 80;
            complex.Single(parameter => parameter.Key == "range").Value.NumberSetValue!.Step = 10;
            complex.Single(parameter => parameter.Key == "group").Value.GroupValue!
                .Single(parameter => parameter.Key == "caption").Value.Model.SimpleValue = "Группа 例";
            var contentReference = complex.Single(parameter => parameter.Key == "contentReference").Value;
            Assert.Multiple(() =>
            {
                Assert.That(contentReference.UsesSimpleValue, Is.True);
                Assert.That(contentReference.ContentValue, Is.Null);
            });
            contentReference.Model.SimpleValue = "question-content-ref-例";

            await qDocument.Save.ExecuteAsync(null);

            await using var stream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(stream);
            var reloadedQuestion = reloaded.Package.Rounds[0].Themes[0].Questions[0];
            Assert.That(
                reloadedQuestion.Script!.Steps,
                Has.Count.EqualTo(3),
                $"Reloaded step types: {string.Join(", ", reloadedQuestion.Script.Steps.Select(step => step.Type))}");
            var reloadedContent = reloadedQuestion.Script!.Steps[0].Parameters[StepParameterNames.Content].ContentValue!;
            var reloadedRange = reloadedQuestion.Script.Steps[2].Parameters["range"].NumberSetValue!;
            var reloadedGroup = reloadedQuestion.Script.Steps[2].Parameters["group"].GroupValue!;

            Assert.Multiple(() =>
            {
                Assert.That(reloadedQuestion.Script.Steps, Has.Count.EqualTo(3));
                Assert.That(reloadedQuestion.Script.Steps[0].Type, Is.EqualTo("showContentCustom"));
                Assert.That(reloadedContent, Has.Count.EqualTo(3));
                Assert.That(reloadedContent[0].Value, Is.EqualTo("Текст сценария 例"));
                Assert.That(reloadedContent[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(7.5)));
                Assert.That(reloadedContent[0].WaitForFinish, Is.False);
                Assert.That(reloadedContent[1].Value, Is.EqualTo("existing image 例.png"));
                Assert.That(reloadedContent[1].IsRef, Is.True);
                Assert.That(reloadedContent[2].Placement, Is.EqualTo(ContentPlacements.Replic));
                Assert.That(reloadedContent[2].Value, Is.EqualTo("Реплика ведущего"));
                Assert.That(reloadedQuestion.Script.Steps[1].Parameters[StepParameterNames.Mode].SimpleValue,
                    Is.EqualTo("direct-custom"));
                Assert.That(reloadedRange.Minimum, Is.EqualTo(10));
                Assert.That(reloadedRange.Maximum, Is.EqualTo(80));
                Assert.That(reloadedRange.Step, Is.EqualTo(10));
                Assert.That(reloadedGroup["caption"].SimpleValue, Is.EqualTo("Группа 例"));
                Assert.That(reloadedQuestion.Script.Steps[2].Parameters["contentReference"].SimpleValue,
                    Is.EqualTo("question-content-ref-例"));
            });
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static SIDocument CreateScriptPackage()
    {
        var document = SIDocument.Create("Scenario package", "Author");
        var round = new Round { Name = "Round" };
        var theme = new Theme { Name = "Theme" };
        var question = new Question { Price = 200, Script = new Script() };
        question.Right.Add("Answer");

        question.Script.Steps.Add(new Step
        {
            Type = StepTypes.ShowContent,
            Parameters =
            {
                [StepParameterNames.Content] = new StepParameter
                {
                    Type = StepParameterTypes.Content,
                    ContentValue = new List<ContentItem>
                    {
                        new() { Type = ContentTypes.Text, Value = "Original text" },
                        new()
                        {
                            Type = ContentTypes.Image,
                            Value = "existing image 例.png",
                            IsRef = true,
                            Placement = ContentPlacements.Screen,
                        },
                    },
                },
            },
        });
        question.Script.Steps.Add(new Step
        {
            Type = StepTypes.AskAnswer,
            Parameters =
            {
                [StepParameterNames.Mode] = new StepParameter { SimpleValue = "direct" },
            },
        });
        question.Script.Steps.Add(new Step
        {
            Type = "custom",
            Parameters =
            {
                ["range"] = new StepParameter
                {
                    Type = StepParameterTypes.NumberSet,
                    NumberSetValue = new NumberSet { Minimum = 1, Maximum = 20, Step = 1 },
                },
                ["group"] = new StepParameter
                {
                    Type = StepParameterTypes.Group,
                    GroupValue = new StepParameters
                    {
                        ["caption"] = new StepParameter { SimpleValue = "Group" },
                    },
                },
                ["contentReference"] = new StepParameter
                {
                    Type = StepParameterTypes.Content,
                    IsRef = true,
                    SimpleValue = "question-content-ref",
                },
            },
        });

        theme.Questions.Add(question);
        round.Themes.Add(theme);
        document.Package.Rounds.Add(round);
        return document;
    }
}
