using Avalonia.Headless.NUnit;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using SIPackages;
using SIPackages.Core;
using SIQuester.Avalonia.Localization;
using SIQuester.Avalonia.Services;
using SIQuester.Avalonia.Views;
using SIQuester.ViewModel;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Model;
using SIStatisticsService.Contract;
using SIStorage.Service.Contract;
using System.Globalization;

namespace SIQuester.Avalonia.Tests;

[TestFixture]
internal sealed class ViewSmokeTests
{
    [AvaloniaTest]
    public void PointSelectionWindow_InstantiatesWithCompiledBindingsAndLocalizedActions()
    {
        var dialog = new PointSelectionWindow("0.25,0.75,2", 0.1, streamInfo: null);

        try
        {
            dialog.Show();
            dialog.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(dialog.Controller.CurrentAnswer, Is.EqualTo("0.25,0.75,2"));
                Assert.That(dialog.GetVisualDescendants().OfType<Button>().Select(button => button.Content),
                    Does.Contain(UiStrings.OK));
                Assert.That(dialog.GetVisualDescendants().OfType<Button>().Select(button => button.Content),
                    Does.Contain(UiStrings.Cancel));
            });
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaTest]
    public async Task PointSelectionWindow_LoadsPackageImageWithoutRetainingSourceStream()
    {
        var imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZqxQAAAAASUVORK5CYII=");
        var source = new MemoryStream(imageBytes, writable: false);
        var dialog = new PointSelectionWindow(
            "",
            0.1,
            new StreamInfo(source, imageBytes.Length));

        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            var image = dialog.FindControl<Image>("TargetImage")!;
            var error = dialog.FindControl<TextBlock>("ImageErrorText")!;

            for (var i = 0; i < 100 && image.Source == null && !error.IsVisible; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Multiple(() =>
            {
                Assert.That(image.Source, Is.Not.Null);
                Assert.That(error.IsVisible, Is.False);
                Assert.That(source.CanRead, Is.False);
            });
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaTest]
    public void MainWindow_InstantiatesWithCompiledBindings()
    {
        var window = new MainWindow();

        Assert.That(window.Content, Is.Not.Null);
    }

    [AvaloniaTest]
    public void Inspector_AcceptsTypedQuestionSelection()
    {
        var question = new QuestionViewModel(new Question { Price = 300 });
        var inspector = new InspectorView { SelectedItem = question };

        Assert.That(inspector.SelectedItem, Is.SameAs(question));
    }

    [AvaloniaTest]
    public void Inspector_MetadataEditorsMutateExistingViewModelsThroughCompiledBindings()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Metadata inspector", "Test author");
        document.Package.Difficulty = 99;
        var round = new Round { Name = "Round" };
        var theme = new Theme { Name = "Theme" };
        theme.Questions.Add(new Question { Price = 100 });
        round.Themes.Add(theme);
        document.Package.Rounds.Add(round);
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Metadata inspector");
        var inspector = new InspectorView { SelectedItem = documentViewModel.Package };
        var window = new Window { Width = 720, Height = 1200, Content = inspector };

        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.That(document.Package.Difficulty, Is.EqualTo(99),
                "Opening the inspector must not normalize an out-of-range legacy value");
            var tagsEditor = inspector.GetVisualDescendants()
                .OfType<StringListEditorView>()
                .Single(editor => editor.Name == "PackageTagsEditor");

            var packageTextEditors = inspector.GetVisualDescendants().OfType<TextBox>().ToArray();
            packageTextEditors.Single(editor => editor.Name == "PackagePublisherEditor").Text = "Avalonia publisher 例";
            packageTextEditors.Single(editor => editor.Name == "PackageContactEditor").Text = "mailto:avalonia@example.test";
            packageTextEditors.Single(editor => editor.Name == "PackageDateEditor").Text = "2026-08-27";
            packageTextEditors.Single(editor => editor.Name == "PackageLanguageEditor").Text = "x-ui-例";
            packageTextEditors.Single(editor => editor.Name == "PackageRestrictionEditor").Text = "18+ 例";
            inspector.GetVisualDescendants().OfType<NumericUpDown>()
                .Single(editor => editor.Name == "PackageDifficultyEditor").Value = 9;
            tagsEditor.Editor!.AddItem.Execute(string.Empty);
            tagsEditor.Editor.CurrentItemValue = "Avalonia tag";
            var packageScroller = inspector.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Where(scrollViewer => ReferenceEquals(scrollViewer.DataContext, documentViewModel.Package))
                .OrderByDescending(scrollViewer => scrollViewer.Extent.Height)
                .First();
            packageScroller.Offset = new global::Avalonia.Vector(0, packageScroller.Extent.Height);
            window.UpdateLayout();
            var metadataEditor = inspector.GetVisualDescendants()
                .OfType<ItemMetadataEditorView>()
                .Single(editor => editor.Name == "PackageMetadataEditor");
            var authorsEditor = metadataEditor.FindControl<StringListEditorView>("AuthorsEditor")!;
            authorsEditor.Editor!.AddItem.Execute(string.Empty);
            authorsEditor.Editor.CurrentItemValue = "author-2";
            metadataEditor.FindControl<TextBox>("CommentsEditor")!.Text = "Package comments";
            metadataEditor.FindControl<TextBox>("ShowmanCommentsEditor")!.Text = "Showman comments";

            Assert.Multiple(() =>
            {
                Assert.That(documentViewModel.Package.Tags, Does.Contain("Avalonia tag"));
                Assert.That(documentViewModel.Package.Info.Authors, Does.Contain("author-2"));
                Assert.That(documentViewModel.Package.Info.Comments.Text, Is.EqualTo("Package comments"));
                Assert.That(documentViewModel.Package.Info.ShowmanComments.Text, Is.EqualTo("Showman comments"));
                Assert.That(document.Package.Publisher, Is.EqualTo("Avalonia publisher 例"));
                Assert.That(document.Package.ContactUri, Is.EqualTo("mailto:avalonia@example.test"));
                Assert.That(document.Package.Date, Is.EqualTo("2026-08-27"));
                Assert.That(document.Package.Language, Is.EqualTo("x-ui-例"));
                Assert.That(document.Package.Restriction, Is.EqualTo("18+ 例"));
                Assert.That(document.Package.Difficulty, Is.EqualTo(9));
            });

            inspector.SelectedItem = documentViewModel.Package.Rounds[0];
            window.UpdateLayout();
            var roundViewModel = documentViewModel.Package.Rounds[0];
            var finalRoundButton = inspector.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, roundViewModel.SetType)
                    && Equals(button.CommandParameter, RoundTypes.Final));
            finalRoundButton.Command!.Execute(finalRoundButton.CommandParameter);
            Assert.That(document.Package.Rounds[0].Type, Is.EqualTo(RoundTypes.Final));
            inspector.GetVisualDescendants().OfType<TextBox>()
                .Single(editor => editor.Name == "RoundTypeEditor").Text = "future-ui-round-例";
            Assert.That(document.Package.Rounds[0].Type, Is.EqualTo("future-ui-round-例"));

            inspector.SelectedItem = documentViewModel.Package.Rounds[0].Themes[0].Questions[0];
            window.UpdateLayout();

            var questionEditors = inspector.GetVisualDescendants().OfType<StringListEditorView>().ToArray();
            var rightAnswersEditor = questionEditors.Single(editor => editor.Header == UiStrings.RightAnswers);
            var wrongAnswersEditor = questionEditors.Single(editor => editor.Header == UiStrings.WrongAnswers);
            rightAnswersEditor.Editor!.AddItem.Execute("Right answer");
            wrongAnswersEditor.Editor!.AddItem.Execute("Wrong answer");

            Assert.Multiple(() =>
            {
                Assert.That(questionEditors.Select(editor => editor.Header), Is.EquivalentTo(new[]
                {
                    UiStrings.RightAnswers,
                    UiStrings.WrongAnswers,
                    UiStrings.Authors,
                    UiStrings.Sources,
                }));
                Assert.That(documentViewModel.Package.Rounds[0].Themes[0].Questions[0].Right,
                    Is.EqualTo(new[] { "Right answer" }));
                Assert.That(documentViewModel.Package.Rounds[0].Themes[0].Questions[0].Wrong,
                    Is.EqualTo(new[] { "Wrong answer" }));
            });

            var question = documentViewModel.Package.Rounds[0].Themes[0].Questions[0];
            question.SetAnswerType.Execute(StepParameterValues.SetAnswerTypeType_ManagedByClient);
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(question.UsesSimpleAnswerCollections, Is.False);
                Assert.That(rightAnswersEditor.IsEffectivelyVisible, Is.False);
                Assert.That(wrongAnswersEditor.IsEffectivelyVisible, Is.False);
            });
        }
        finally
        {
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Inspector_QualityControlCommandsUpdateCanonicalStateAndVisibility()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Quality inspector", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Quality inspector");
        var package = documentViewModel.Package;
        var inspector = new InspectorView { SelectedItem = package };
        var window = new Window { Width = 720, Height = 1200, Content = inspector };

        try
        {
            window.Show();
            window.UpdateLayout();
            var enableButton = inspector.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, package.EnableQualityControl));
            var disableButton = inspector.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, package.DisableQualityControl));

            Assert.Multiple(() =>
            {
                Assert.That(enableButton.IsEffectivelyVisible, Is.True);
                Assert.That(disableButton.IsEffectivelyVisible, Is.False);
                Assert.That(enableButton.Content, Is.EqualTo(UiStrings.EnableQualityControl));
            });

            await package.EnableQualityControl.ExecuteAsync(null);
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(document.Package.HasQualityControl, Is.True);
                Assert.That(enableButton.IsEffectivelyVisible, Is.False);
                Assert.That(disableButton.IsEffectivelyVisible, Is.True);
                Assert.That(disableButton.Content, Is.EqualTo(UiStrings.DisableQualityControl));
            });

            disableButton.Command!.Execute(disableButton.CommandParameter);
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(document.Package.HasQualityControl, Is.False);
                Assert.That(enableButton.IsEffectivelyVisible, Is.True);
                Assert.That(disableButton.IsEffectivelyVisible, Is.False);
            });
        }
        finally
        {
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Inspector_PackageLogoPickerLoadsBoundedPreviewAndRemovesReference()
    {
        var imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZqxQAAAAASUVORK5CYII=");
        var source = new MemoryStream(imageBytes, writable: false);
        var pickedFile = new PickedFile(
            localPath: null,
            displayName: "логотип 例.png",
            extension: ".png",
            _ => ValueTask.FromResult<Stream>(source));
        var filePicker = Substitute.For<IFilePickerService>();
        filePicker.PickOpenFilesAsync(
                Arg.Any<OpenFilePickerRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<PickedFile>>([pickedFile]));
        using var serviceProvider = CreateServiceProvider(filePickerService: filePicker);
        using var document = SIDocument.Create("Logo inspector", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Logo inspector");
        var package = documentViewModel.Package;
        var inspector = new InspectorView { SelectedItem = package };
        var window = new Window { Width = 720, Height = 1200, Content = inspector };

        try
        {
            window.Show();
            window.UpdateLayout();
            var preview = inspector.GetVisualDescendants()
                .OfType<PackageLogoPreview>()
                .Single();
            var image = preview.FindControl<Image>("LogoImage")!;
            var loading = preview.FindControl<TextBlock>("LoadingText")!;
            var error = preview.FindControl<TextBlock>("ImageErrorText")!;
            var selectButton = inspector.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, package.SelectLogo));
            var removeButton = inspector.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, package.RemoveLogo));

            Assert.Multiple(() =>
            {
                Assert.That(preview.IsEffectivelyVisible, Is.False);
                Assert.That(removeButton.IsEffectivelyVisible, Is.False);
                Assert.That(selectButton.Content, Is.EqualTo(UiStrings.SelectLogo));
                Assert.That(removeButton.Content, Is.EqualTo(UiStrings.RemoveLogo));
            });

            await package.SelectLogo.ExecuteAsync(null);
            window.UpdateLayout();

            for (var i = 0; i < 100 && image.Source == null && !error.IsVisible; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Multiple(() =>
            {
                Assert.That(source.CanRead, Is.False, "The portal stream must be released after staging");
                Assert.That(document.Package.Logo, Is.EqualTo("@логотип 例.png"));
                Assert.That(package.HasLogo, Is.True);
                Assert.That(preview.IsEffectivelyVisible, Is.True);
                Assert.That(image.Source, Is.Not.Null);
                Assert.That(loading.IsVisible, Is.False);
                Assert.That(error.IsVisible, Is.False);
                Assert.That(removeButton.IsEffectivelyVisible, Is.True);
            });

            removeButton.Command!.Execute(removeButton.CommandParameter);
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(document.Package.Logo, Is.Empty);
                Assert.That(package.HasLogo, Is.False);
                Assert.That(preview.IsEffectivelyVisible, Is.False);
                Assert.That(removeButton.IsEffectivelyVisible, Is.False);
            });

            await filePicker.Received(1).PickOpenFilesAsync(
                Arg.Is<OpenFilePickerRequest>(request =>
                    !request.AllowMultiple
                    && request.FileTypes.Single().Extensions.Contains("png")),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task PackageLogoPreview_ExternalLinkIsNotLoadedAsApplicationMedia()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("External logo", "Test author");
        document.Package.Logo = "https://example.invalid/package-logo.png";
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "External logo");
        var preview = new PackageLogoPreview { Package = documentViewModel.Package };
        var window = new Window { Width = 720, Height = 240, Content = preview };

        try
        {
            window.Show();
            window.UpdateLayout();
            var image = preview.FindControl<Image>("LogoImage")!;
            var error = preview.FindControl<TextBlock>("ImageErrorText")!;

            for (var i = 0; i < 100 && image.Source == null && !error.IsVisible; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Multiple(() =>
            {
                Assert.That(documentViewModel.Package.HasLogo, Is.True);
                Assert.That(documentViewModel.Package.OpenLogoStream(), Is.Null,
                    "External package URLs must not be opened through the embedded-media preview path");
                Assert.That(image.Source, Is.Null);
                Assert.That(error.IsVisible, Is.True);
            });
        }
        finally
        {
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public void Inspector_ScenarioEditorsMutateCanonicalScriptThroughCompiledBindings()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Scenario inspector", "Test author");
        var round = new Round { Name = "Round" };
        var theme = new Theme { Name = "Theme" };
        var question = new Question { Price = 100, Script = new Script() };
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
                        new() { Type = ContentTypes.Text, Value = "Original content" },
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
        question.Right.Add("Answer");
        theme.Questions.Add(question);
        round.Themes.Add(theme);
        document.Package.Rounds.Add(round);
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Scenario inspector");
        var questionViewModel = documentViewModel.Package.Rounds[0].Themes[0].Questions[0];
        var inspector = new InspectorView { SelectedItem = questionViewModel };
        var window = new Window { Width = 760, Height = 1400, Content = inspector };

        try
        {
            window.Show();
            window.UpdateLayout();

            var scenarioEditor = inspector.GetVisualDescendants().OfType<ScenarioEditorView>().Single();
            var contentEditor = scenarioEditor.GetVisualDescendants()
                .OfType<ContentItemsEditorView>()
                .Single(editor => editor.IsEffectivelyVisible);
            contentEditor.Editor!.CurrentPosition = 0;
            window.UpdateLayout();
            contentEditor.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(control => control.Name == "ContentValueEditor" && control.IsEffectivelyVisible)
                .Text = "Текст из Avalonia 例";

            var simpleEditor = scenarioEditor.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(control => control.Name == "SimpleParameterEditor" && control.IsEffectivelyVisible);
            simpleEditor.Text = "direct-ui";
            contentEditor.Editor.AddVoice.Execute(null);
            contentEditor.Editor.CurrentItem!.Model.Value = "Реплика из Avalonia";

            Assert.Multiple(() =>
            {
                Assert.That(questionViewModel.ScriptSteps, Has.Count.EqualTo(2));
                Assert.That(question.Script.Steps[0].Parameters[StepParameterNames.Content].ContentValue![0].Value,
                    Is.EqualTo("Текст из Avalonia 例"));
                Assert.That(question.Script.Steps[0].Parameters[StepParameterNames.Content].ContentValue![1].Value,
                    Is.EqualTo("Реплика из Avalonia"));
                Assert.That(question.Script.Steps[1].Parameters[StepParameterNames.Mode].SimpleValue,
                    Is.EqualTo("direct-ui"));
            });
        }
        finally
        {
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public void Inspector_ScriptAndParameterCrudMutatesCanonicalCollectionsThroughCompiledBindings()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Script CRUD inspector", "Test author");
        var round = new Round { Name = "Round" };
        var theme = new Theme { Name = "Theme" };
        var question = new Question { Price = 100, Script = new Script() };
        question.Script.Steps.Add(new Step
        {
            Type = StepTypes.AskAnswer,
            Parameters =
            {
                [StepParameterNames.Mode] = new StepParameter { SimpleValue = "direct" },
            },
        });
        question.Right.Add("Answer");
        theme.Questions.Add(question);
        round.Themes.Add(theme);
        document.Package.Rounds.Add(round);
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Script CRUD inspector");
        var questionViewModel = documentViewModel.Package.Rounds[0].Themes[0].Questions[0];
        var inspector = new InspectorView { SelectedItem = questionViewModel };
        var window = new Window { Width = 820, Height = 1600, Content = inspector };

        try
        {
            window.Show();
            window.UpdateLayout();
            var scenarioEditor = inspector.GetVisualDescendants().OfType<ScenarioEditorView>().Single();
            var addStepButton = scenarioEditor.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, questionViewModel.ScriptSteps.AddStep));
            addStepButton.Command!.Execute(addStepButton.CommandParameter);
            window.UpdateLayout();

            var addedStep = questionViewModel.ScriptSteps[^1];
            var parameterEditor = scenarioEditor.GetVisualDescendants()
                .OfType<StepParametersEditorView>()
                .Single(editor => ReferenceEquals(editor.Editor, addedStep.Parameters));
            parameterEditor.FindControl<TextBox>("NewParameterNameEditor")!.Text = "ui-parameter-例";
            var addSimpleButton = parameterEditor.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, UiStrings.AddSimpleParameter));
            addSimpleButton.Command!.Execute(addSimpleButton.CommandParameter);
            window.UpdateLayout();

            var parameterRecord = addedStep.Parameters.Single(parameter => parameter.Key == "ui-parameter-例");
            var valueEditor = parameterEditor.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(textBox => textBox.Name == "SimpleParameterEditor"
                    && textBox.IsEffectivelyVisible
                    && textBox.DataContext is StepParameterRecord record
                    && record.Key == parameterRecord.Key);
            valueEditor.Text = "Значение из Avalonia 例";

            var addedStepExpander = scenarioEditor.GetVisualDescendants()
                .OfType<Expander>()
                .Single(expander => ReferenceEquals(expander.DataContext, addedStep));
            var moveUpButton = addedStepExpander.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, addedStep.MoveUp));
            moveUpButton.Command!.Execute(moveUpButton.CommandParameter);
            window.UpdateLayout();
            parameterEditor = scenarioEditor.GetVisualDescendants()
                .OfType<StepParametersEditorView>()
                .Single(editor => ReferenceEquals(editor.Editor, addedStep.Parameters));
            addedStepExpander = scenarioEditor.GetVisualDescendants()
                .OfType<Expander>()
                .Single(expander => ReferenceEquals(expander.DataContext, addedStep));

            Assert.Multiple(() =>
            {
                Assert.That(questionViewModel.ScriptSteps, Has.Count.EqualTo(2));
                Assert.That(question.Script.Steps[0], Is.SameAs(addedStep.Model));
                Assert.That(question.Script.Steps[0].Parameters["ui-parameter-例"].SimpleValue,
                    Is.EqualTo("Значение из Avalonia 例"));
            });

            var deleteParameterButton = parameterEditor.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, addedStep.Parameters.DeleteParameter)
                    && button.CommandParameter is StepParameterRecord record
                    && record.Key == parameterRecord.Key);
            deleteParameterButton.Command!.Execute(deleteParameterButton.CommandParameter);
            Assert.That(addedStep.Parameters.Model, Does.Not.ContainKey(parameterRecord.Key));
            documentViewModel.OperationsManager.Undo.Execute(null);
            Assert.That(addedStep.Parameters.Model, Contains.Key(parameterRecord.Key));

            var deleteStepButton = addedStepExpander.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, addedStep.Remove));
            deleteStepButton.Command!.Execute(deleteStepButton.CommandParameter);
            Assert.That(questionViewModel.ScriptSteps, Has.Count.EqualTo(1));
            documentViewModel.OperationsManager.Undo.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(questionViewModel.ScriptSteps, Has.Count.EqualTo(2));
                Assert.That(question.Script.Steps[0], Is.SameAs(addedStep.Model));
            });
        }
        finally
        {
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public void Inspector_NonTextAnswerEditorsMutateCanonicalParametersThroughCompiledBindings()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Non-text inspector", "Test author");
        var round = new Round { Name = "Round" };
        var theme = new Theme { Name = "Theme" };
        var question = new Question { Price = 100 };
        question.Right.Add("Original answer");
        theme.Questions.Add(question);
        round.Themes.Add(theme);
        document.Package.Rounds.Add(round);
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Non-text inspector");
        var questionViewModel = documentViewModel.Package.Rounds[0].Themes[0].Questions[0];
        var inspector = new InspectorView { SelectedItem = questionViewModel };
        var window = new Window { Width = 760, Height = 1400, Content = inspector };

        try
        {
            window.Show();
            window.UpdateLayout();
            var answerTypeButtons = inspector.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => ReferenceEquals(button.Command, questionViewModel.SetAnswerType))
                .ToArray();

            var numberButton = answerTypeButtons.Single(button => Equals(
                button.CommandParameter,
                StepParameterValues.SetAnswerTypeType_Number));
            numberButton.Command!.Execute(numberButton.CommandParameter);
            window.UpdateLayout();

            var numericEditor = inspector.GetVisualDescendants()
                .OfType<NumericAnswerEditorView>()
                .Single(control => control.IsEffectivelyVisible);
            var numericInputs = numericEditor.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            numericInputs[0].Value = -314;
            numericInputs[1].Value = 7;

            Assert.Multiple(() =>
            {
                Assert.That(numericInputs, Has.Length.EqualTo(2));
                Assert.That(questionViewModel.Right, Is.EqualTo(new[] { "-314" }));
                Assert.That(questionViewModel.Parameters.Model[QuestionParameterNames.AnswerDeviation].SimpleValue,
                    Is.EqualTo("7"));
            });

            var selectButton = answerTypeButtons.Single(button => Equals(
                button.CommandParameter,
                StepParameterValues.SetAnswerTypeType_Select));
            selectButton.Command!.Execute(selectButton.CommandParameter);
            window.UpdateLayout();

            var optionsEditor = inspector.GetVisualDescendants()
                .OfType<AnswerOptionsEditorView>()
                .Single(control => control.IsEffectivelyVisible);
            var options = optionsEditor.Options!.GroupValue!;
            var originalOptionCount = options.Count;
            var addOptionButton = optionsEditor.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, UiStrings.AddOption));
            addOptionButton.Command!.Execute(addOptionButton.CommandParameter);
            window.UpdateLayout();
            var markRightButton = optionsEditor.GetVisualDescendants()
                .OfType<Button>()
                .Last(button => Equals(button.Content, UiStrings.MarkRightAnswer));
            var markedOption = (StepParameterRecord)markRightButton.CommandParameter!;
            markRightButton.Command!.Execute(markRightButton.CommandParameter);

            Assert.Multiple(() =>
            {
                Assert.That(questionViewModel.Right, Is.EqualTo(new[] { markedOption.Key }));
                Assert.That(optionsEditor.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => Equals(text.Text, markedOption.Key)), Is.True);
            });

            var pointButton = answerTypeButtons.Single(button => Equals(
                button.CommandParameter,
                StepParameterValues.SetAnswerTypeType_Point));
            pointButton.Command!.Execute(pointButton.CommandParameter);
            window.UpdateLayout();
            var pointEditor = inspector.GetVisualDescendants()
                .OfType<PointAnswerEditorView>()
                .Single(control => control.IsEffectivelyVisible);
            var pointEditorHost = pointEditor.GetVisualAncestors()
                .OfType<ContentControl>()
                .Single(control => control.Name == "PointAnswerEditorHost");
            pointEditor.FindControl<TextBox>("PointAnswerEditor")!.Text = "0.46,0.7";

            Assert.Multiple(() =>
            {
                Assert.That(questionViewModel.AnswerOptions, Is.Null);
                Assert.That(options, Has.Count.EqualTo(originalOptionCount + 1));
                Assert.That(questionViewModel.IsPointAnswer, Is.True);
                Assert.That(questionViewModel.Right, Is.EqualTo(new[] { "0.46,0.7" }));
                Assert.That(questionViewModel.Parameters.Model[QuestionParameterNames.AnswerDeviation].SimpleValue,
                    Is.EqualTo("0"));
            });

            var managedButton = answerTypeButtons.Single(button => Equals(
                button.CommandParameter,
                StepParameterValues.SetAnswerTypeType_ManagedByClient));
            managedButton.Command!.Execute(managedButton.CommandParameter);
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(questionViewModel.IsManagedByClient, Is.True);
                Assert.That(questionViewModel.Right, Is.Empty);
                Assert.That(pointEditorHost.IsVisible, Is.False);
                Assert.That(inspector.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.IsEffectivelyVisible && Equals(text.Text, UiStrings.ClientManagedDescription)),
                    Is.True);
            });
        }
        finally
        {
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public void DocumentEditor_InitialSelectionFlowsToTypedInspector()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Selection test", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Selection test");
        var view = new DocumentEditorView { DataContext = documentViewModel };
        var window = new Window { Content = view };

        window.Show();
        view.UpdateLayout();

        var inspector = view.FindControl<InspectorView>("Inspector");
        Assert.Multiple(() =>
        {
            Assert.That(documentViewModel.ActiveNode, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector, Is.Not.Null);
            Assert.That(inspector!.SelectedItem, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector.GetVisualDescendants().OfType<TextBox>(), Is.Not.Empty);
        });

        window.Close();
        documentViewModel.Dispose();
    }

    [AvaloniaTest]
    public void DocumentEditor_DocumentAttachedAfterViewIsShown_PreservesInitialSelection()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Delayed selection test", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Delayed selection test");
        var view = new DocumentEditorView();
        var window = new Window { Content = view };

        window.Show();
        view.DataContext = documentViewModel;
        view.UpdateLayout();

        var inspector = view.FindControl<InspectorView>("Inspector");
        Assert.Multiple(() =>
        {
            Assert.That(documentViewModel.ActiveNode, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector!.SelectedItem, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector.GetVisualDescendants().OfType<TextBox>(), Is.Not.Empty);
        });

        window.Close();
        documentViewModel.Dispose();
    }

    [AvaloniaTest]
    public void DocumentEditor_FlatWorkspacePersistsLayoutAndScaleAndRoutesKeyboardOperations()
    {
        using var serviceProvider = CreateServiceProvider();
        var package = SIDocument.Create("Flat view", "Test author");
        var round = new Round { Name = "Unicode Раунд" };
        var theme = new Theme { Name = "Theme" };
        theme.Questions.Add(new Question { Price = 100 });
        theme.Questions.Add(new Question { Price = 200 });
        round.Themes.Add(theme);
        package.Package.Rounds.Add(round);
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Flat view");
        var previousView = AppSettings.Default.View;
        var previousLayout = AppSettings.Default.FlatLayoutMode;
        var previousScale = AppSettings.Default.FlatScale;
        AppSettings.Default.View = ViewMode.TreeFull;
        AppSettings.Default.FlatLayoutMode = FlatLayoutMode.Table;
        AppSettings.Default.FlatScale = FlatScale.Theme;
        var view = new DocumentEditorView { DataContext = documentViewModel };
        var window = new Window { Width = 1100, Height = 700, Content = view };

        try
        {
            window.Show();
            window.UpdateLayout();
            var flatButton = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.CommandParameter, ViewMode.Flat));
            var tree = view.FindControl<TreeView>("Navigator")!;
            var flatView = view.FindControl<FlatDocumentView>("FlatWorkspace")!;
            var treeInspector = view.FindControl<InspectorView>("Inspector")!;
            var flatInspector = view.FindControl<InspectorView>("FlatInspector")!;

            flatButton.Command!.Execute(flatButton.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(AppSettings.Default.View, Is.EqualTo(ViewMode.Flat));
                Assert.That(documentViewModel.IsFlatView, Is.True);
                Assert.That(tree.IsVisible, Is.True, "the hierarchy remains available beside the main flat workspace");
                Assert.That(flatView.IsVisible, Is.True);
                Assert.That(treeInspector.IsVisible, Is.False);
                Assert.That(flatInspector.IsVisible, Is.True);
                Assert.That(flatView.GetVisualDescendants()
                    .OfType<Border>()
                    .Count(border => border.DataContext is QuestionViewModel && border.Classes.Contains("flat-question-card")),
                    Is.EqualTo(2));
                Assert.That(flatView.GetVisualDescendants()
                    .OfType<Border>()
                    .Count(border => border.Classes.Contains("flat-drop-target")), Is.EqualTo(3));
                Assert.That(flatView.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Any(text => Equals(text.Text, "Unicode Раунд")), Is.True);
                Assert.That(flatView.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Any(text => Equals(text.Text, UiStrings.FlatKeyboardHint)), Is.True);
            });

            var layoutButtons = flatView.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.CommandParameter is FlatLayoutMode)
                .ToDictionary(button => (FlatLayoutMode)button.CommandParameter!);
            var scaleButtons = flatView.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.CommandParameter is FlatScale)
                .ToDictionary(button => (FlatScale)button.CommandParameter!);

            Assert.That(scaleButtons.Keys, Is.EquivalentTo(Enum.GetValues<FlatScale>()));

            layoutButtons[FlatLayoutMode.List].Command!.Execute(FlatLayoutMode.List);
            scaleButtons[FlatScale.Package].Command!.Execute(FlatScale.Package);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Multiple(() =>
            {
                Assert.That(AppSettings.Default.FlatLayoutMode, Is.EqualTo(FlatLayoutMode.List));
                Assert.That(flatView.FindControl<ScrollViewer>("PackageScaleSurface")!.IsVisible, Is.True);
                Assert.That(flatView.FindControl<Grid>("RoundScaleSurface")!.IsVisible, Is.False);
            });

            scaleButtons[FlatScale.Round].Command!.Execute(FlatScale.Round);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Multiple(() =>
            {
                Assert.That(flatView.FindControl<Grid>("RoundScaleSurface")!.IsVisible, Is.True);
                Assert.That(flatView.FindControl<ListBox>("RoundListLayout")!.IsVisible, Is.True);
            });

            scaleButtons[FlatScale.Theme].Command!.Execute(FlatScale.Theme);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.That(flatView.FindControl<Grid>("DetailedScaleSurface")!.IsVisible, Is.True);

            scaleButtons[FlatScale.Question].Command!.Execute(FlatScale.Question);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var firstQuestion = documentViewModel.Package.Rounds[0].Themes[0].Questions[0];
            var firstQuestionCard = flatView.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => ReferenceEquals(border.DataContext, firstQuestion)
                    && border.Classes.Contains("flat-question-card")
                    && border.IsEffectivelyVisible);
            firstQuestionCard.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.That(documentViewModel.Package.Rounds[0].Themes[0].Questions[1], Is.SameAs(firstQuestion));

            firstQuestionCard = flatView.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => ReferenceEquals(border.DataContext, firstQuestion)
                    && border.Classes.Contains("flat-question-card")
                    && border.IsEffectivelyVisible);
            firstQuestionCard.Focus();
            window.KeyPress(Key.D, RawInputModifiers.Control, PhysicalKey.D, "d");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(AppSettings.Default.FlatScale, Is.EqualTo(FlatScale.Question));
                Assert.That(documentViewModel.Package.Rounds[0].Themes[0].Questions, Has.Count.EqualTo(3));
                Assert.That(flatInspector.SelectedItem, Is.SameAs(documentViewModel.ActiveNode));
                Assert.That(flatView.GetVisualDescendants().OfType<VirtualizingStackPanel>().Any(), Is.True);
                Assert.That(flatView.GetVisualDescendants().OfType<Button>()
                    .Count(button => button.IsEffectivelyVisible
                        && Equals(button.GetValue(AutomationProperties.NameProperty), UiStrings.DuplicateQuestion)),
                    Is.EqualTo(3));
            });

            var duplicateButton = flatView.GetVisualDescendants().OfType<Button>()
                .First(button => button.IsEffectivelyVisible
                    && Equals(button.GetValue(AutomationProperties.NameProperty), UiStrings.DuplicateQuestion));
            duplicateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.That(documentViewModel.Package.Rounds[0].Themes[0].Questions, Has.Count.EqualTo(4),
                "the visible action routes through the document command instead of mutating in code-behind");
        }
        finally
        {
            AppSettings.Default.View = previousView;
            AppSettings.Default.FlatLayoutMode = previousLayout;
            AppSettings.Default.FlatScale = previousScale;
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task DocumentEditor_SearchBarRoutesFocusAndPublishesLatestResultState()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Search inspector", "Test author");
        var round = new Round { Name = "Round" };
        var theme = new Theme { Name = "Theme" };
        var question = new Question { Price = 100 };
        question.Right.Add("search-target-例");
        theme.Questions.Add(question);
        round.Themes.Add(theme);
        document.Package.Rounds.Add(round);
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Search inspector");
        var view = new DocumentEditorView { DataContext = documentViewModel };
        var window = new Window { Width = 900, Height = 600, Content = view };

        try
        {
            window.Show();
            window.UpdateLayout();
            var searchBox = view.FindControl<TextBox>("SearchBox")!;
            var noResults = view.FindControl<TextBlock>("NoSearchResultsText")!;
            var previousButton = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, documentViewModel.PreviousSearchResult));
            var nextButton = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, documentViewModel.NextSearchResult));
            var clearButton = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, documentViewModel.ClearSearchText));

            var editorTextBox = view.GetVisualDescendants()
                .OfType<TextBox>()
                .First(textBox => !ReferenceEquals(textBox, searchBox));
            editorTextBox.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.That(editorTextBox.IsFocused, Is.True);
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            Dispatcher.UIThread.RunJobs();
            Assert.That(searchBox.IsFocused, Is.True);

            editorTextBox.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.F, RawInputModifiers.Meta, PhysicalKey.F, "f");
            Dispatcher.UIThread.RunJobs();
            Assert.That(searchBox.IsFocused, Is.True, "Meta+F must route to search on macOS");

            searchBox.Text = "search-target-例";

            for (var i = 0; i < 100 && documentViewModel.SearchResults?.Query != "search-target-例"; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(documentViewModel.SearchResults?.Results, Has.Count.EqualTo(1));
                Assert.That(noResults.IsEffectivelyVisible, Is.False);
                Assert.That(previousButton.IsEnabled, Is.True);
                Assert.That(nextButton.IsEnabled, Is.True);
                Assert.That(clearButton.IsEnabled, Is.True);
                Assert.That(searchBox.Bounds.Width, Is.GreaterThanOrEqualTo(120));
            });

            searchBox.Text = "missing-target";

            for (var i = 0;
                i < 100 && (!documentViewModel.SearchFailed || documentViewModel.NextSearchResult.CanBeExecuted);
                i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(documentViewModel.SearchFailed, Is.True);
                Assert.That(noResults.IsEffectivelyVisible, Is.True);
                Assert.That(previousButton.IsEnabled, Is.False);
                Assert.That(nextButton.IsEnabled, Is.False);
                Assert.That(clearButton.IsEnabled, Is.True);
                Assert.That(noResults.Text, Is.EqualTo(UiStrings.NoSearchResults));
            });
        }
        finally
        {
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public void MainWindow_DocumentOpenedAfterStartup_PreservesInitialSelection()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Startup selection test", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Startup selection test");
        var mainViewModel = CreateMainViewModel(serviceProvider);
        var window = new MainWindow { DataContext = mainViewModel };

        window.Show();
        mainViewModel.DocList.Add(documentViewModel);
        window.UpdateLayout();

        var inspector = window.GetVisualDescendants()
            .OfType<InspectorView>()
            .Single(candidate => candidate.Name == "Inspector");
        Assert.Multiple(() =>
        {
            Assert.That(mainViewModel.ActiveDocument, Is.SameAs(documentViewModel));
            Assert.That(documentViewModel.ActiveNode, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector.SelectedItem, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector.GetVisualDescendants().OfType<TextBox>(), Is.Not.Empty);
        });

        window.DataContext = null;
        window.Close();
        documentViewModel.Dispose();
    }

    [AvaloniaTest]
    public void MainWindow_CloseWithNoDocuments_CompletesWithoutReentrantPrompt()
    {
        using var serviceProvider = CreateServiceProvider();
        var window = new MainWindow { DataContext = CreateMainViewModel(serviceProvider) };

        window.Show();
        window.Close();

        Assert.That(window.IsVisible, Is.False);
    }

    [AvaloniaTest]
    public void MainWindow_CloseWithNoDocuments_RunsSettingsPersistenceOnce()
    {
        using var serviceProvider = CreateServiceProvider();
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var persistenceCalls = 0;
        var window = new MainWindow(_ =>
        {
            persistenceCalls++;
            return ValueTask.CompletedTask;
        })
        {
            DataContext = mainViewModel,
        };

        window.Show();
        window.Close();

        Assert.Multiple(() =>
        {
            Assert.That(window.IsVisible, Is.False);
            Assert.That(persistenceCalls, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public async Task RecoveryCenter_RendersPerEntryCommandsAndStaleState()
    {
        using var serviceProvider = CreateServiceProvider();
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var recoveryService = Substitute.For<IDocumentRecoveryService>();
        var externalLauncher = serviceProvider.GetRequiredService<IExternalLauncher>();
        var entry = new DocumentRecoveryEntry(
            Guid.NewGuid().ToString("N"),
            Path.Combine(Path.GetTempPath(), "recovery", "document.siq"),
            Path.Combine(Path.GetTempPath(), "packages", "saved.siq"),
            "Recovered package",
            DateTimeOffset.UtcNow,
            2048,
            new string('0', 64),
            IsStale: true);
        var recoveryEntry = new RecoveryEntryViewModel(
            entry,
            recoveryService,
            externalLauncher,
            (_, _) => Task.CompletedTask,
            _ => { },
            _ => Task.CompletedTask);
        var previewDocument = SIDocument.Create("Preview package", "Test author");
        recoveryService
            .LoadAsync(entry, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(previewDocument));
        mainViewModel.RecoveryEntries.Add(recoveryEntry);
        var window = new MainWindow { DataContext = mainViewModel };

        try
        {
            window.Show();
            window.UpdateLayout();
            var recoveryCenter = window.FindControl<Border>("RecoveryCenter")
                ?? throw new AssertionException("Recovery center was not created.");
            var restoreButton = recoveryCenter.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, recoveryEntry.Restore) && button.IsVisible);

            Assert.Multiple(() =>
            {
                Assert.That(recoveryCenter.IsVisible, Is.True);
                Assert.That(restoreButton.IsEnabled, Is.True);
                Assert.That(restoreButton.Content, Is.EqualTo(UiStrings.RestoreAsCopy));
                Assert.That(recoveryCenter.GetVisualDescendants().OfType<Button>()
                    .Any(button => ReferenceEquals(button.Command, recoveryEntry.Preview)), Is.True);
                Assert.That(recoveryCenter.GetVisualDescendants().OfType<Button>()
                    .Any(button => ReferenceEquals(button.Command, recoveryEntry.Reveal)), Is.True);
            });

            await recoveryEntry.Preview.ExecuteAsync(null);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(recoveryEntry.IsPreviewVisible, Is.True);
                Assert.That(recoveryCenter.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.Command, recoveryEntry.Preview)).IsVisible, Is.True);
                Assert.That(recoveryCenter.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.Command, recoveryEntry.Restore) && button.IsVisible)
                    .Content, Is.EqualTo(UiStrings.RestoreAsCopy));
                Assert.That(recoveryCenter.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.Command, recoveryEntry.Reveal)).IsVisible, Is.True);
                Assert.That(recoveryCenter.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.Command, recoveryEntry.RequestDiscard)).IsVisible, Is.True);
            });
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public void EmptyState_RendersRecentFilesWithOpenCommand()
    {
        using var serviceProvider = CreateServiceProvider();
        var recentPath = Path.Combine(Path.GetTempPath(), "Папка с пробелами", "пакет 例.siq");
        AppSettings.Default.History.Add(recentPath);
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var window = new MainWindow { DataContext = mainViewModel };

        try
        {
            window.Show();
            window.UpdateLayout();
            var recentButton = window.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.CommandParameter, recentPath));

            Assert.Multiple(() =>
            {
                Assert.That(recentButton.Command, Is.SameAs(mainViewModel.OpenRecent));
                Assert.That(ToolTip.GetTip(recentButton), Is.EqualTo(recentPath));
            });
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public void SettingsView_UpdatesThemeAndLanguageThroughCompiledBindings()
    {
        AppSettings.Default = AppSettings.Create();
        var viewModel = new SettingsViewModel(Substitute.For<IPlatformService>());
        var view = new SettingsView { DataContext = viewModel };
        var window = new Window { Content = view };

        window.Show();
        view.UpdateLayout();

        var selectors = view.GetVisualDescendants().OfType<ComboBox>().ToArray();
        Assert.That(selectors, Has.Length.EqualTo(2));

        selectors[0].SelectedItem = DesktopThemePreference.Dark;
        selectors[1].SelectedItem = "ru-RU";

        Assert.Multiple(() =>
        {
            Assert.That(AppSettings.Default.DesktopTheme, Is.EqualTo(DesktopThemePreference.Dark));
            Assert.That(AppSettings.Default.Language, Is.EqualTo("ru-RU"));
        });

        window.Close();
    }

    [AvaloniaTest]
    public async Task AvaloniaClipboardService_RoundTripsTypedFormats()
    {
        var window = new Window();
        window.Show();
        var service = new AvaloniaClipboardService(() => window);
        var customFormat = new ClipboardCustomFormat(
            "SIQuester.Tests.v1",
            ClipboardCustomDataKind.Binary);
        var imagePng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester clipboard тест {Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(filePath, "clipboard file");
            await service.WriteAsync(new ClipboardWriteRequest
            {
                Text = "Буфер обмена",
                FilePaths = [filePath],
                ImagePng = imagePng,
                CustomData = [new ClipboardCustomData(customFormat, new byte[] { 7, 8, 9 })],
            });

            var text = await service.ReadTextAsync();
            var filePaths = await service.ReadFilePathsAsync();
            var roundTrippedImagePng = await service.ReadImagePngAsync();
            var customData = await service.ReadCustomDataAsync(customFormat);

            Assert.Multiple(() =>
            {
                Assert.That(text, Is.EqualTo("Буфер обмена"));
                Assert.That(filePaths, Does.Contain(filePath));
                Assert.That(roundTrippedImagePng, Is.EqualTo(imagePng));
                Assert.That(customData, Is.EqualTo(new byte[] { 7, 8, 9 }));
            });
        }
        finally
        {
            await service.ClearAsync();
            window.Close();
            File.Delete(filePath);
        }
    }

    [AvaloniaTest]
    public async Task CtrlC_WithDocumentFocus_InvokesDocumentCopy()
    {
        var clipboardService = Substitute.For<IClipboardService>();
        clipboardService
            .WriteAsync(Arg.Any<ClipboardWriteRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        using var serviceProvider = CreateServiceProvider(clipboardService);
        using var document = SIDocument.Create("Shortcut test", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Shortcut test");
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var window = new MainWindow { DataContext = mainViewModel };

        try
        {
            window.Show();
            mainViewModel.DocList.Add(documentViewModel);
            window.UpdateLayout();

            var tree = window.GetVisualDescendants().OfType<TreeView>().Single();
            tree.Focus();
            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            await Dispatcher.UIThread.InvokeAsync(() => { });

            await clipboardService.Received(1).WriteAsync(
                Arg.Is<ClipboardWriteRequest>(request => request.CustomData.Count > 0),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task CtrlC_WithFocusedTextBox_DoesNotInvokeDocumentCopy()
    {
        var clipboardService = Substitute.For<IClipboardService>();
        clipboardService
            .WriteAsync(Arg.Any<ClipboardWriteRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        using var serviceProvider = CreateServiceProvider(clipboardService);
        using var document = SIDocument.Create("Text shortcut test", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Text shortcut test");
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var window = new MainWindow { DataContext = mainViewModel };

        try
        {
            window.Show();
            mainViewModel.DocList.Add(documentViewModel);
            window.UpdateLayout();

            var textBox = window.GetVisualDescendants().OfType<TextBox>().First();
            textBox.Text = "Selected text";
            textBox.SelectionStart = 0;
            textBox.SelectionEnd = textBox.Text.Length;
            textBox.Focus();
            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            await Dispatcher.UIThread.InvokeAsync(() => { });

            await clipboardService.DidNotReceive().WriteAsync(
                Arg.Any<ClipboardWriteRequest>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public void CoreViews_InstantiateWithoutNativeServices()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new NewPackageView(), Is.Not.Null);
            Assert.That(new DocumentEditorView(), Is.Not.Null);
            Assert.That(new SettingsView(), Is.Not.Null);
            Assert.That(new MessageDialogWindow(), Is.Not.Null);
            Assert.That(
                new OptionDialogWindow("Recovery", [new DialogOption("close", "Close")]),
                Is.Not.Null);
        });
    }

    [Test]
    public void RussianResources_AreAvailable()
    {
        var previousCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.That(UiStrings.New, Is.EqualTo("Создать"));
            Assert.That(UiStrings.Save, Is.EqualTo("Сохранить"));
            Assert.That(UiStrings.Options, Is.EqualTo("Настройки"));
            Assert.That(UiStrings.Authors, Is.EqualTo("Авторы"));
            Assert.That(UiStrings.ShowmanComments, Is.EqualTo("Комментарии ведущему"));
            Assert.That(UiStrings.EnableQualityControl, Is.EqualTo("Включить контроль качества"));
            Assert.That(UiStrings.DisableQualityControl, Is.EqualTo("Выключить контроль качества"));
            Assert.That(UiStrings.PackageLogo, Is.EqualTo("Логотип пакета"));
            Assert.That(UiStrings.SelectLogo, Is.EqualTo("Выбрать логотип"));
            Assert.That(UiStrings.RemoveLogo, Is.EqualTo("Удалить логотип"));
            Assert.That(UiStrings.Search, Is.EqualTo("Поиск"));
            Assert.That(UiStrings.NoSearchResults, Is.EqualTo("Совпадений не найдено"));
            Assert.That(UiStrings.PreviousSearchResult, Is.EqualTo("Предыдущее совпадение"));
            Assert.That(UiStrings.NextSearchResult, Is.EqualTo("Следующее совпадение"));
            Assert.That(UiStrings.ClearSearch, Is.EqualTo("Очистить поиск"));
            Assert.That(UiStrings.RightAnswers, Is.EqualTo("Правильные ответы"));
            Assert.That(
                new DesktopThemeLabelConverter().Convert(
                    DesktopThemePreference.System,
                    typeof(string),
                    null,
                    CultureInfo.CurrentUICulture),
                Is.EqualTo("Системная тема"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static ServiceProvider CreateServiceProvider(
        IClipboardService? clipboardService = null,
        IFilePickerService? filePickerService = null)
    {
        AppSettings.Default = new AppSettings();
        var services = new ServiceCollection();
        services.AddSIQuester();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        var appPaths = Substitute.For<IAppPaths>();
        appPaths.RecoveryDirectory.Returns(Path.Combine(Path.GetTempPath(), "SIQuester.Avalonia.Tests", Guid.NewGuid().ToString("N")));
        appPaths.TemporaryMediaDirectory.Returns(Path.Combine(
            Path.GetTempPath(),
            "SIQuester.Avalonia.Tests",
            Guid.NewGuid().ToString("N"),
            "media"));
        services.AddSingleton(appPaths);
        services.AddSingleton(clipboardService ?? Substitute.For<IClipboardService>());
        services.AddSingleton(filePickerService ?? Substitute.For<IFilePickerService>());
        services.AddSingleton(Substitute.For<IDialogService>());
        services.AddSingleton(Substitute.For<IApplicationLifetimeService>());
        services.AddSingleton(Substitute.For<IMediaMaterializationService>());
        services.AddSingleton(Substitute.For<IPlatformService>());
        var platformCapabilities = Substitute.For<IPlatformCapabilities>();
        platformCapabilities.SupportsRecoveryManagementUi.Returns(true);
        services.AddSingleton(platformCapabilities);
        services.AddSingleton(Substitute.For<IExternalLauncher>());
        services.AddSingleton(Substitute.For<ISIStatisticsServiceClient>());
        services.AddSingleton(Substitute.For<ISIStorageServiceClient>());

        var templatesRepository = Substitute.For<IPackageTemplatesRepository>();
        templatesRepository.Templates.Returns(new List<PackageTemplate>());
        services.AddSingleton(templatesRepository);
        services.AddSingleton(serviceProvider => new StorageContextViewModel(
            serviceProvider.GetRequiredService<ISIStorageServiceClient>(),
            AppSettings.Default,
            serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<StorageContextViewModel>()));

        return services.BuildServiceProvider();
    }

    private static MainViewModel CreateMainViewModel(IServiceProvider serviceProvider) => new(
        Array.Empty<string>(),
        new AppOptions(),
        serviceProvider.GetRequiredService<IClipboardService>(),
        serviceProvider,
        serviceProvider.GetRequiredService<IPlatformService>(),
        serviceProvider.GetRequiredService<IDocumentViewModelFactory>(),
        serviceProvider.GetRequiredService<ILoggerFactory>(),
        serviceProvider.GetRequiredService<IFilePickerService>(),
        serviceProvider.GetRequiredService<IDialogService>(),
        serviceProvider.GetRequiredService<IApplicationLifetimeService>(),
        serviceProvider.GetRequiredService<IDocumentRecoveryService>(),
        serviceProvider.GetRequiredService<IPlatformCapabilities>(),
        serviceProvider.GetRequiredService<IExternalLauncher>());
}
