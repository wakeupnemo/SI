using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using NSubstitute;
using NUnit.Framework;
using SIQuester.Avalonia.Controls;
using SIQuester.Avalonia.Localization;
using SIQuester.Avalonia.Views;
using SIQuester.Model;
using SIQuester.ViewModel;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using System.Text;
using Utils.Commands;

namespace SIQuester.Avalonia.Tests;

[TestFixture]
internal sealed class ImportTextViewTests
{
    [AvaloniaTest]
    public void MainWindowDataTemplateResolvesTextImportWorkspace()
    {
        using var workspace = CreateWorkspace(Substitute.For<IFilePickerService>());
        var window = new MainWindow
        {
            Content = new ContentControl { Content = workspace },
        };

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.That(window.GetVisualDescendants().OfType<ImportTextView>().Single().DataContext,
                Is.SameAs(workspace));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task NeutralFileCommand_TransitionsCompiledViewToUnicodePreview()
    {
        var picker = Substitute.For<IFilePickerService>();
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetPreamble()
            .Concat("Пакет 例"u8.ToArray())
            .ToArray();
        picker.PickOpenFilesAsync(Arg.Any<OpenFilePickerRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<PickedFile>>(
                [new PickedFile(null, "пакет 例.txt", ".txt", _ =>
                    ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)))]));
        using var workspace = CreateWorkspace(picker);
        var view = new ImportTextView { DataContext = workspace };
        var window = new Window { Width = 960, Height = 700, Content = view };

        try
        {
            window.Show();
            window.UpdateLayout();
            var selectFile = view.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, workspace.SelectFile));

            await ((IAsyncCommand)selectFile.Command!).ExecuteAsync(selectFile.CommandParameter);
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(view.FindControl<Grid>("InitialStatePanel")!.IsEffectivelyVisible, Is.False);
                Assert.That(view.FindControl<Grid>("ImportFileStatePanel")!.IsEffectivelyVisible, Is.True);
                Assert.That(view.FindControl<ComboBox>("EncodingSelector")!.IsEffectivelyVisible, Is.True);
                Assert.That(view.FindControl<TextBox>("ImportedTextPreview")!.Text, Is.EqualTo("Пакет 例"));
                Assert.That(view.GetVisualDescendants().OfType<Button>().Select(button => button.Content),
                    Does.Contain(UiStrings.StartImport));
                Assert.That(workspace.Run, Is.InstanceOf<IAsyncCommand>());
                Assert.That(workspace.ApproveImportAndStart, Is.InstanceOf<IAsyncCommand>());
                Assert.That(view.GetVisualDescendants().OfType<Button>().Any(button =>
                    ReferenceEquals(button.Command, workspace.ApproveImportAndStart)), Is.True);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void ExplicitStatePanelsFollowWorkspaceStateWithoutTemplateConverters()
    {
        using var workspace = CreateWorkspace(Substitute.For<IFilePickerService>());
        var view = new ImportTextView { DataContext = workspace };
        var window = new Window { Width = 960, Height = 700, Content = view };

        try
        {
            window.Show();
            workspace.State = ImportTextViewModel.UIState.Split;
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(view.FindControl<Grid>("SplitStatePanel")!.IsEffectivelyVisible, Is.True);
                Assert.That(view.FindControl<Grid>("ParseStatePanel")!.IsEffectivelyVisible, Is.False);
            });

            workspace.State = ImportTextViewModel.UIState.Parse;
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(view.FindControl<Grid>("SplitStatePanel")!.IsEffectivelyVisible, Is.False);
                Assert.That(view.FindControl<Grid>("ParseStatePanel")!.IsEffectivelyVisible, Is.True);
                Assert.That(view.FindControl<ListBox>("SourceFragments")!.DataContext, Is.SameAs(workspace));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void StructuralTemplateActionsSynchronizeAndDetachThroughExistingCommands()
    {
        using var workspace = CreateWorkspace(Substitute.For<IFilePickerService>());
        var template = workspace.Templates[0];
        template.Transform = "[ignoresp]start";
        template.Variants.Add("[m]<PName>");
        workspace.Free = true;
        workspace.State = ImportTextViewModel.UIState.Parse;
        var view = new ImportTextView { DataContext = workspace };
        var window = new Window { Width = 1200, Height = 1000, Content = view };

        try
        {
            window.Show();
            window.UpdateLayout();
            var templateView = view.GetVisualDescendants().OfType<SpardTemplateEditorView>().First();
            var session = templateView.Session!;
            var editor = templateView.GetVisualDescendants().OfType<SpardEditorControl>().Single();
            var tokenPresenter = templateView.GetVisualDescendants().OfType<SpardTokenPresenter>().Single();
            session.Editor.SetCaret(session.Editor.DisplayText.Length);
            var aliasButton = templateView.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, template.InsertAlias)
                    && Equals(button.CommandParameter, "PName"));
            var optionalButton = templateView.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, template.InsertOptional));

            aliasButton.Command!.Execute(aliasButton.CommandParameter);
            optionalButton.Command!.Execute(optionalButton.CommandParameter);

            Assert.Multiple(() =>
            {
                Assert.That(template.Transform, Is.EqualTo(session.Editor.SerializedText));
                Assert.That(editor.Text, Does.Contain("start"));
                Assert.That(editor.Text, Does.Contain("()?"));
                Assert.That(tokenPresenter.Children, Has.Count.EqualTo(session.Editor.Tokens.Count));
                Assert.That(tokenPresenter.Children.OfType<Border>()
                    .Any(border => border.Classes.Contains("alias")), Is.True);
                Assert.That(tokenPresenter.Children.OfType<Border>()
                    .Count(border => border.Classes.Contains("optional")), Is.EqualTo(2));
                Assert.That(templateView.GetVisualDescendants().OfType<Button>()
                    .Any(button => ReferenceEquals(button.Command, template.ChangeTemplate)), Is.True);
            });

            template.ChangeTemplate.Execute("[m]<PName>");
            Assert.That(editor.Text, Is.EqualTo(session.Editor.DisplayText));

            window.Close();
            Assert.Multiple(() =>
            {
                Assert.That(templateView.Session, Is.Null);
                Assert.That(editor.Text, Is.Empty);
                Assert.That(tokenPresenter.Children, Is.Empty);
            });
            template.Transform = "[ignoresp]detached";
            Assert.That(editor.Text, Is.Empty);
        }
        finally
        {
            window.Close();
        }
    }

    private static ImportTextViewModel CreateWorkspace(IFilePickerService picker)
    {
        AppSettings.Default = new AppSettings();
        return new ImportTextViewModel(
            new AppOptions(),
            Substitute.For<IClipboardService>(),
            Substitute.For<IDocumentViewModelFactory>(),
            picker,
            Substitute.For<IDialogService>(),
            new InlineUiDispatcher());
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return ValueTask.CompletedTask;
        }
    }
}
