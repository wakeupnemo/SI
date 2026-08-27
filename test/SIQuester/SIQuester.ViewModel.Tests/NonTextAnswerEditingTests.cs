using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class NonTextAnswerEditingTests
{
    [Test]
    public void NumericAndPointEditors_UpdateCanonicalAnswerData()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = CreatePackageWithQuestions(2);
        using var qDocument = factory.CreateViewModelFor(document, "Non-text answers");
        var numericQuestion = qDocument.Package.Rounds[0].Themes[0].Questions[0];
        var pointQuestion = qDocument.Package.Rounds[0].Themes[0].Questions[1];

        numericQuestion.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Number);
        var numericAnswer = numericQuestion.NumericAnswer!;
        numericAnswer.Answer = int.MinValue;
        numericAnswer.Deviation = int.MaxValue;

        pointQuestion.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Point);
        var pointAnswer = pointQuestion.PointAnswer!;
        var pointSelection = new PointSelectionController(pointAnswer.Answer, pointAnswer.Deviation);
        pointSelection.SelectFromViewport(46, 65, 100, 100, 400, 300);
        pointSelection.CurrentDeviation = 0.05;
        pointSelection.ApplyTo(pointAnswer);

        Assert.Multiple(() =>
        {
            Assert.That(numericQuestion.IsNumericAnswer, Is.True);
            Assert.That(numericQuestion.NumericAnswer, Is.SameAs(numericAnswer));
            Assert.That(numericQuestion.Right, Is.EqualTo(new[] { int.MinValue.ToString() }));
            Assert.That(numericQuestion.Parameters.Model[QuestionParameterNames.AnswerDeviation].SimpleValue,
                Is.EqualTo(int.MaxValue.ToString()));
            Assert.That(pointQuestion.IsPointAnswer, Is.True);
            Assert.That(pointQuestion.PointAnswer, Is.SameAs(pointAnswer));
            Assert.That(pointQuestion.Right, Is.EqualTo(new[] { "0.46,0.7,1.33" }));
            Assert.That(pointQuestion.Parameters.Model[QuestionParameterNames.AnswerDeviation].SimpleValue,
                Is.EqualTo("0.05"));
            Assert.That(qDocument.OperationsManager.Undo.CanExecute(null), Is.True);
        });
    }

    [Test]
    public void SelectEditor_AddsEditsMarksAndDeletesOptionsWithoutResettingCurrentType()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = CreatePackageWithQuestions(1);
        using var qDocument = factory.CreateViewModelFor(document, "Select answers");
        var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];

        question.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Select);
        var optionsParameter = question.AnswerOptions!;
        var options = optionsParameter.GroupValue!;
        var originalCount = options.Count;
        options.AddItem.Execute(question);
        Assert.That(options, Has.Count.EqualTo(originalCount + 1));
        qDocument.OperationsManager.Undo.Execute(null);
        Assert.That(options, Has.Count.EqualTo(originalCount));
        qDocument.OperationsManager.Redo.Execute(null);
        Assert.That(options, Has.Count.EqualTo(originalCount + 1));
        var addedOption = options[^1];
        addedOption.Value.ContentValue!.CurrentPosition = 0;
        addedOption.Value.ContentValue.CurrentItem!.Model.Value = "Добавленный вариант 例";
        options.MakeRight.Execute(addedOption);
        options.DeleteItem.Execute(options[0]);
        var expectedRightKey = options[^1].Key;

        qDocument.OperationsManager.Undo.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(options, Has.Count.EqualTo(originalCount + 1));
            Assert.That(question.Right, Is.EqualTo(new[] { addedOption.Key }));
        });
        qDocument.OperationsManager.Redo.Execute(null);

        question.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Select);

        Assert.Multiple(() =>
        {
            Assert.That(question.IsSelectAnswer, Is.True);
            Assert.That(question.AnswerOptions, Is.SameAs(optionsParameter));
            Assert.That(options, Has.Count.EqualTo(originalCount));
            Assert.That(options[^1].Value.Model.ContentValue![0].Value, Is.EqualTo("Добавленный вариант 例"));
            Assert.That(question.Right, Is.EqualTo(new[] { expectedRightKey }));
            Assert.That(question.Wrong, Is.Empty);
        });
    }

    [Test]
    public void AnswerTypeUndoRedo_NotifiesTypedInspectorFlags()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = CreatePackageWithQuestions(1);
        using var qDocument = factory.CreateViewModelFor(document, "Answer type undo");
        var question = qDocument.Package.Rounds[0].Themes[0].Questions[0];
        var changedProperties = new List<string?>();
        question.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        question.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Number);
        changedProperties.Clear();
        question.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Select);
        changedProperties.Clear();

        qDocument.OperationsManager.Undo.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(question.IsNumericAnswer, Is.True);
            Assert.That(question.IsSelectAnswer, Is.False);
            Assert.That(changedProperties, Does.Contain(nameof(QuestionViewModel.IsNumericAnswer)));
            Assert.That(changedProperties, Does.Contain(nameof(QuestionViewModel.IsSelectAnswer)));
        });

        changedProperties.Clear();
        qDocument.OperationsManager.Redo.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(question.IsNumericAnswer, Is.False);
            Assert.That(question.IsSelectAnswer, Is.True);
            Assert.That(changedProperties, Does.Contain(nameof(QuestionViewModel.IsNumericAnswer)));
            Assert.That(changedProperties, Does.Contain(nameof(QuestionViewModel.IsSelectAnswer)));
        });
    }

    [Test]
    public async Task EveryNonTextAnswerType_SaveAndReloadsThroughSIDocument()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = CreatePackageWithQuestions(4);
        using var qDocument = factory.CreateViewModelFor(document, "Ответы 例");
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester non-text answers {Guid.NewGuid():N} 例.siq");
        qDocument.Path = filePath;

        try
        {
            var questions = qDocument.Package.Rounds[0].Themes[0].Questions;

            questions[0].SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Number);
            questions[0].NumericAnswer!.Answer = -42;
            questions[0].NumericAnswer!.Deviation = 3;

            questions[1].SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Point);
            var pointSelection = new PointSelectionController(
                questions[1].PointAnswer!.Answer,
                questions[1].PointAnswer!.Deviation);
            pointSelection.SelectFromViewport(46, 65, 100, 100, 400, 300);
            pointSelection.CurrentDeviation = 0.05;
            pointSelection.ApplyTo(questions[1].PointAnswer!);

            questions[2].SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_Select);
            var options = questions[2].AnswerOptions!.GroupValue!;
            options.AddItem.Execute(questions[2]);
            var customOption = options[^1];
            customOption.Value.ContentValue!.CurrentPosition = 0;
            customOption.Value.ContentValue.CurrentItem!.Model.Value = "Вариант 例";
            options.MakeRight.Execute(customOption);

            questions[3].Wrong.Add("Legacy wrong answer");
            questions[3].SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_ManagedByClient);

            await qDocument.Save.ExecuteAsync(null);

            await using var stream = File.OpenRead(filePath);
            using var reloaded = SIDocument.Load(stream);
            var reloadedQuestions = reloaded.Package.Rounds[0].Themes[0].Questions;
            var reloadedOptions = reloadedQuestions[2].Parameters[QuestionParameterNames.AnswerOptions].GroupValue!;

            Assert.Multiple(() =>
            {
                Assert.That(reloadedQuestions[0].Parameters[QuestionParameterNames.AnswerType].SimpleValue,
                    Is.EqualTo(StepParameterValues.SetAnswerTypeType_Number));
                Assert.That(reloadedQuestions[0].Right, Is.EqualTo(new[] { "-42" }));
                Assert.That(reloadedQuestions[0].Parameters[QuestionParameterNames.AnswerDeviation].SimpleValue,
                    Is.EqualTo("3"));
                Assert.That(reloadedQuestions[1].Parameters[QuestionParameterNames.AnswerType].SimpleValue,
                    Is.EqualTo(StepParameterValues.SetAnswerTypeType_Point));
                Assert.That(reloadedQuestions[1].Right, Is.EqualTo(new[] { "0.46,0.7,1.33" }));
                Assert.That(reloadedQuestions[1].Parameters[QuestionParameterNames.AnswerDeviation].SimpleValue,
                    Is.EqualTo("0.05"));
                Assert.That(reloadedQuestions[2].Parameters[QuestionParameterNames.AnswerType].SimpleValue,
                    Is.EqualTo(StepParameterValues.SetAnswerTypeType_Select));
                Assert.That(reloadedOptions.Values.Select(value => value.ContentValue![0].Value),
                    Does.Contain("Вариант 例"));
                Assert.That(reloadedQuestions[2].Right[0], Is.EqualTo(reloadedOptions.Keys.Last()));
                Assert.That(reloadedQuestions[3].Parameters[QuestionParameterNames.AnswerType].SimpleValue,
                    Is.EqualTo(StepParameterValues.SetAnswerTypeType_ManagedByClient));
                // SIPackages deliberately normalizes every reloaded question to at least one right-answer entry.
                Assert.That(reloadedQuestions[3].Right, Is.EqualTo(new[] { string.Empty }));
                Assert.That(reloadedQuestions[3].Wrong, Is.Empty);
                Assert.That(reloadedQuestions[3].Parameters.ContainsKey(QuestionParameterNames.AnswerDeviation),
                    Is.False);
                Assert.That(reloadedQuestions[3].Parameters.ContainsKey(QuestionParameterNames.AnswerOptions),
                    Is.False);
            });
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static SIDocument CreatePackageWithQuestions(int questionCount)
    {
        var document = TestHelper.CreateSimpleTestPackage();
        var questions = document.Package.Rounds[0].Themes[0].Questions;

        while (questions.Count < questionCount)
        {
            questions.Add(questions[0].Clone());
        }

        return document;
    }
}
