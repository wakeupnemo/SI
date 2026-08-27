using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class ScriptCrudEditingTests
{
    [Test]
    public void ScriptStepAddMoveRemove_UndoRedoKeepsCanonicalOrderAndListeners()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = CreateScriptPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Script CRUD");
        var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];

        question.ScriptSteps.AddStep.Execute(null);
        var addedStep = question.ScriptSteps[^1];
        addedStep.Model.Type = "custom-added-例";
        addedStep.MoveUp.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(question.ScriptSteps[0], Is.SameAs(addedStep));
            Assert.That(question.Model.Script!.Steps[0], Is.SameAs(addedStep.Model));
            Assert.That(question.Model.Script.Steps.Select(step => step.Type),
                Is.EqualTo(new[] { "custom-added-例", "future-step-例" }));
            Assert.That(addedStep.MoveUp.CanExecute(null), Is.False);
            Assert.That(addedStep.MoveDown.CanExecute(null), Is.True);
        });

        qDocument.OperationsManager.Undo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(question.ScriptSteps[1], Is.SameAs(addedStep));
            Assert.That(question.Model.Script!.Steps[1], Is.SameAs(addedStep.Model));
        });

        qDocument.OperationsManager.Redo.Execute(null);
        Assert.That(question.ScriptSteps[0], Is.SameAs(addedStep));

        addedStep.MoveDown.Execute(null);
        Assert.That(question.ScriptSteps[1], Is.SameAs(addedStep));
        qDocument.OperationsManager.Undo.Execute(null);
        Assert.That(question.ScriptSteps[0], Is.SameAs(addedStep));
        qDocument.OperationsManager.Redo.Execute(null);
        Assert.That(question.ScriptSteps[1], Is.SameAs(addedStep));

        addedStep.Remove.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(question.ScriptSteps, Has.Count.EqualTo(1));
            Assert.That(question.Model.Script!.Steps, Has.Count.EqualTo(1));
        });

        qDocument.OperationsManager.Undo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(question.ScriptSteps[1], Is.SameAs(addedStep));
            Assert.That(question.Model.Script!.Steps[1], Is.SameAs(addedStep.Model));
        });

        addedStep.Model.Type = "custom-after-undo-例";
        Assert.That(qDocument.OperationsManager.Undo.CanExecute(null), Is.True);
        qDocument.OperationsManager.Undo.Execute(null);
        Assert.That(addedStep.Model.Type, Is.EqualTo("custom-added-例"));
    }

    [Test]
    public void RemovingEqualScriptStep_RemovesTheSelectedCanonicalInstance()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = CreateScriptPackage();
        var firstStep = document.Package.Rounds[0].Themes[0].Questions[0].Script!.Steps[0];
        var equalStep = new Step { Type = firstStep.Type };
        equalStep.Parameters["future-parameter"] = new StepParameter
        {
            Type = "future-kind-例",
            SimpleValue = "opaque-original-例",
        };
        document.Package.Rounds[0].Themes[0].Questions[0].Script!.Steps.Add(equalStep);
        using var qDocument = factory.CreateViewModelFor(document, "Equal script steps");
        var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];

        question.ScriptSteps[1].Remove.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(question.ScriptSteps, Has.Count.EqualTo(1));
            Assert.That(question.Model.Script!.Steps, Has.Count.EqualTo(1));
            Assert.That(question.Model.Script.Steps[0], Is.SameAs(firstStep));
        });
    }

    [Test]
    public async Task GenericParameterCrud_AllKindsAndUnknownType_SaveAndReload()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = CreateScriptPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Параметры 例");
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester script CRUD {Guid.NewGuid():N} 例.siq");
        qDocument.Path = filePath;

        try
        {
            var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];
            var futureStep = question.ScriptSteps[0];
            var unknownParameter = futureStep.Parameters.Single(parameter => parameter.Key == "future-parameter");
            Assert.That(unknownParameter.Value.UsesSimpleValue, Is.True);
            unknownParameter.Value.Model.SimpleValue = "opaque-edited-例";

            question.ScriptSteps.AddStep.Execute(null);
            var addedStep = question.ScriptSteps[^1];
            addedStep.Model.Type = "custom-script-step-例";
            var parameters = addedStep.Parameters;

            Assert.That(parameters.Model, Contains.Key(StepParameterNames.Content));
            parameters.NewParameterName = StepParameterNames.Content;
            Assert.That(parameters.AddGenericParameter.CanExecute(StepParameterTypes.Simple), Is.False);

            AddParameter(parameters, "simple-例", StepParameterTypes.Simple);
            parameters.Single(parameter => parameter.Key == "simple-例").Value.Model.SimpleValue = "simple value 例";

            AddParameter(parameters, "content-例", StepParameterTypes.Content);
            var content = parameters.Single(parameter => parameter.Key == "content-例").Value.ContentValue!;
            content.AddText.Execute(null);
            content.CurrentItem!.Model.Value = "content value 例";

            AddParameter(parameters, "group-例", StepParameterTypes.Group);
            var group = parameters.Single(parameter => parameter.Key == "group-例").Value.GroupValue!;
            AddParameter(group, "nested-例", StepParametersViewModel.ReferenceParameterKind);
            var nestedReference = group.Single(parameter => parameter.Key == "nested-例").Value;
            nestedReference.Model.SimpleValue = "question-data-reference-例";

            AddParameter(parameters, "range-例", StepParameterTypes.NumberSet);
            var range = parameters.Single(parameter => parameter.Key == "range-例").Value.NumberSetValue!;
            range.Minimum = int.MinValue;
            range.Maximum = int.MaxValue;
            range.Step = 7;

            AddParameter(parameters, "reference-例", StepParametersViewModel.ReferenceParameterKind);
            parameters.Single(parameter => parameter.Key == "reference-例").Value.Model.SimpleValue = "answerType-例";

            AddParameter(parameters, "temporary-例", StepParameterTypes.Simple);
            var temporary = parameters.Single(parameter => parameter.Key == "temporary-例");
            parameters.DeleteParameter.Execute(temporary);
            Assert.That(parameters.Model, Does.Not.ContainKey("temporary-例"));
            qDocument.OperationsManager.Undo.Execute(null);
            Assert.That(parameters.Model, Contains.Key("temporary-例"));
            qDocument.OperationsManager.Redo.Execute(null);
            Assert.That(parameters.Model, Does.Not.ContainKey("temporary-例"));

            addedStep.MoveUp.Execute(null);
            await qDocument.Save.ExecuteAsync(null);

            await using var stream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(stream);
            var reloadedSteps = reloaded.Package.Rounds[0].Themes[0].Questions[0].Script!.Steps;
            var reloadedParameters = reloadedSteps[0].Parameters;

            Assert.Multiple(() =>
            {
                Assert.That(reloadedSteps.Select(step => step.Type),
                    Is.EqualTo(new[] { "custom-script-step-例", "future-step-例" }));
                Assert.That(reloadedParameters[StepParameterNames.Content].ContentValue, Is.Empty);
                Assert.That(reloadedParameters["simple-例"].SimpleValue, Is.EqualTo("simple value 例"));
                Assert.That(reloadedParameters["content-例"].ContentValue![0].Value, Is.EqualTo("content value 例"));
                Assert.That(reloadedParameters["group-例"].GroupValue!["nested-例"].IsRef, Is.True);
                Assert.That(reloadedParameters["group-例"].GroupValue!["nested-例"].SimpleValue,
                    Is.EqualTo("question-data-reference-例"));
                Assert.That(reloadedParameters["range-例"].NumberSetValue!.Minimum, Is.EqualTo(int.MinValue));
                Assert.That(reloadedParameters["range-例"].NumberSetValue!.Maximum, Is.EqualTo(int.MaxValue));
                Assert.That(reloadedParameters["range-例"].NumberSetValue!.Step, Is.EqualTo(7));
                Assert.That(reloadedParameters["reference-例"].IsRef, Is.True);
                Assert.That(reloadedParameters["reference-例"].SimpleValue, Is.EqualTo("answerType-例"));
                Assert.That(reloadedParameters, Does.Not.ContainKey("temporary-例"));
                Assert.That(reloadedSteps[1].Parameters["future-parameter"].Type, Is.EqualTo("future-kind-例"));
                Assert.That(reloadedSteps[1].Parameters["future-parameter"].SimpleValue,
                    Is.EqualTo("opaque-edited-例"));
            });
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static void AddParameter(StepParametersViewModel parameters, string name, string parameterKind)
    {
        parameters.NewParameterName = name;
        Assert.That(parameters.AddGenericParameter.CanExecute(parameterKind), Is.True, name);
        parameters.AddGenericParameter.Execute(parameterKind);
        Assert.That(parameters.Model, Contains.Key(name));
    }

    private static SIDocument CreateScriptPackage()
    {
        var document = SIDocument.Create("Script CRUD package", "Author");
        var round = new Round { Name = "Round" };
        var theme = new Theme { Name = "Theme" };
        var question = new Question { Price = 100, Script = new Script() };
        var step = new Step { Type = "future-step-例" };
        step.Parameters["future-parameter"] = new StepParameter
        {
            Type = "future-kind-例",
            SimpleValue = "opaque-original-例",
        };
        question.Script.Steps.Add(step);
        question.Right.Add("Answer");
        theme.Questions.Add(question);
        round.Themes.Add(theme);
        document.Package.Rounds.Add(round);
        return document;
    }
}
