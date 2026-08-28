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
    public void ContentMoments_GroupCanonicalItemsByWaitBoundaryAndPlacement()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = CreateStoryboardPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Storyboard moments");
        var content = qDocument.Package.Rounds[0].Themes[0].Questions[0].LegacyContent!;

        Assert.Multiple(() =>
        {
            Assert.That(content.CurrentItem, Is.SameAs(content[0]));
            Assert.That(content.CurrentPosition, Is.Zero);
            Assert.That(content[0].IsCurrent, Is.True);
            Assert.That(content.Moments, Has.Count.EqualTo(3));
            Assert.That(content.Moments[0].ScreenItems.Select(item => item.Model.Value),
                Is.EqualTo(new[] { "Question text", "question.png" }));
            Assert.That(content.Moments[1].BackgroundItems.Single().Model.Value, Is.EqualTo("music.ogg"));
            Assert.That(content.Moments[1].ReplicItems.Single().Model.Value, Is.EqualTo("Showman line"));
            Assert.That(content.Moments[2].OtherItems.Single().Model.Placement, Is.EqualTo("future-placement"));
            Assert.That(content.Moments[0].CanMergeWithNext, Is.True);
            Assert.That(content.Moments[2].CanMergeWithNext, Is.False);
        });

        content.Moments[0].MergeWithNext.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(content[1].Model.WaitForFinish, Is.False);
            Assert.That(content.Moments, Has.Count.EqualTo(2));
            Assert.That(content.Moments[0].Items, Has.Count.EqualTo(4));
            Assert.That(qDocument.OperationsManager.Undo.CanExecute(null), Is.True);
        });

        content[1].SeparateFromNext.Execute(null);
        Assert.That(content.Moments, Has.Count.EqualTo(3));

        qDocument.OperationsManager.Undo.Execute(null);
        Assert.That(content.Moments, Has.Count.EqualTo(2));
        qDocument.OperationsManager.Undo.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(content[1].Model.WaitForFinish, Is.True);
            Assert.That(content.Moments, Has.Count.EqualTo(3));
        });

        content.CurrentItem = content[0];
        content.Moments[0].MoveLater.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(content.Select(item => item.Model.Value), Is.EqualTo(new[]
            {
                "music.ogg", "Showman line", "Question text", "question.png", "opaque",
            }));
            Assert.That(content.Moments, Has.Count.EqualTo(3));
            Assert.That(content.CurrentItem!.Model.Value, Is.EqualTo("Question text"));
            Assert.That(content.CurrentPosition, Is.EqualTo(2));
        });

        qDocument.OperationsManager.Undo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(content.Select(item => item.Model.Value), Is.EqualTo(new[]
            {
                "Question text", "question.png", "music.ogg", "Showman line", "opaque",
            }));
            Assert.That(content.CurrentItem, Is.SameAs(content[0]));
            Assert.That(content.CurrentPosition, Is.Zero);
            Assert.That(content[0].IsCurrent, Is.True);
        });

        qDocument.OperationsManager.Redo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(content.CurrentItem!.Model.Value, Is.EqualTo("Question text"));
            Assert.That(content.CurrentPosition, Is.EqualTo(2));
            Assert.That(content[2].IsCurrent, Is.True);
        });
        qDocument.OperationsManager.Undo.Execute(null);

        content.MoveRight.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(content.Select(item => item.Model.Value).Take(2),
                Is.EqualTo(new[] { "question.png", "Question text" }),
                "The first index-based command after undo must act on the visibly selected card.");
            Assert.That(content.CurrentItem!.Model.Value, Is.EqualTo("Question text"));
            Assert.That(content.CurrentPosition, Is.EqualTo(1));
        });
        qDocument.OperationsManager.Undo.Execute(null);

        content[^1].Model.WaitForFinish = false;
        content.Moments[^1].MoveEarlier.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(content.Moments, Has.Count.EqualTo(3),
                "Moving a trailing implicit moment must create the boundary needed at its new position.");
            Assert.That(content[2].Model.Value, Is.EqualTo("opaque"));
            Assert.That(content[2].Model.WaitForFinish, Is.True);
        });

        qDocument.OperationsManager.Undo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(content[^1].Model.Value, Is.EqualTo("opaque"));
            Assert.That(content[^1].Model.WaitForFinish, Is.False);
        });
    }

    [Test]
    public async Task StoryboardCommands_QuestionAndPostAnswerContent_SaveCanonicalRoundTrip()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = CreateStoryboardPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Лента 例");
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester storyboard {Guid.NewGuid():N} 例.siq");
        qDocument.Path = filePath;

        try
        {
            var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];
            question.LegacyContent!.Moments[0].MergeWithNext.Execute(null);
            question.PostAnswerContent!.Moments[0].MergeWithNext.Execute(null);

            await qDocument.Save.ExecuteAsync(null);

            await using var stream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(stream);
            var reloadedQuestion = reloaded.Package.Rounds[0].Themes[0].Questions[0];
            var questionContent = reloadedQuestion.Parameters[QuestionParameterNames.Question].ContentValue!;
            var answerContent = reloadedQuestion.Parameters[QuestionParameterNames.Answer].ContentValue!;

            Assert.Multiple(() =>
            {
                Assert.That(questionContent.Select(item => item.WaitForFinish),
                    Is.EqualTo(new[] { false, false, false, true, true }));
                Assert.That(questionContent[2].Placement, Is.EqualTo(ContentPlacements.Background));
                Assert.That(questionContent[4].Placement, Is.EqualTo("future-placement"));
                Assert.That(questionContent[4].Type, Is.EqualTo("future-content"));
                Assert.That(answerContent.Select(item => item.WaitForFinish), Is.EqualTo(new[] { false, true, true }));
                Assert.That(answerContent.Select(item => item.Value),
                    Is.EqualTo(new[] { "Answer caption", "answer.png", "explanation.mp4" }));
            });
        }
        finally
        {
            File.Delete(filePath);
        }
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

    private static SIDocument CreateStoryboardPackage()
    {
        var document = SIDocument.Create("Storyboard package", "Author");
        var round = new Round { Name = "Round" };
        var theme = new Theme { Name = "Theme" };
        var question = new Question { Price = 300 };
        question.Parameters[QuestionParameterNames.Question] = new StepParameter
        {
            Type = StepParameterTypes.Content,
            ContentValue =
            [
                new ContentItem { Type = ContentTypes.Text, Value = "Question text", WaitForFinish = false },
                new ContentItem { Type = ContentTypes.Image, Value = "question.png", IsRef = true },
                new ContentItem
                {
                    Type = ContentTypes.Audio,
                    Value = "music.ogg",
                    IsRef = true,
                    Placement = ContentPlacements.Background,
                    WaitForFinish = false,
                },
                new ContentItem { Type = ContentTypes.Text, Value = "Showman line", Placement = ContentPlacements.Replic },
                new ContentItem { Type = "future-content", Value = "opaque", Placement = "future-placement" },
            ],
        };
        question.Parameters[QuestionParameterNames.Answer] = new StepParameter
        {
            Type = StepParameterTypes.Content,
            ContentValue =
            [
                new ContentItem { Type = ContentTypes.Text, Value = "Answer caption" },
                new ContentItem { Type = ContentTypes.Image, Value = "answer.png", IsRef = true },
                new ContentItem { Type = ContentTypes.Video, Value = "explanation.mp4", IsRef = true },
            ],
        };
        question.Right.Add("Answer");
        theme.Questions.Add(question);
        round.Themes.Add(theme);
        document.Package.Rounds.Add(round);
        return document;
    }
}
