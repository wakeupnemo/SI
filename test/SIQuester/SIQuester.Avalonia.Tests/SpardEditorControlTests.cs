using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using NUnit.Framework;
using SIQuester.Avalonia.Controls;
using SIQuester.Avalonia.Localization;
using SIQuester.ViewModel.Services;

namespace SIQuester.Avalonia.Tests;

[TestFixture]
internal sealed class SpardEditorControlTests
{
    [AvaloniaTest]
    public void TextInputDeleteAndEnter_RouteThroughStructuralController()
    {
        var controller = new SpardEditorController(name => name == "QText" ? "Question" : name);
        controller.Load("[m]<QText>");
        var editor = new SpardEditorControl { Controller = controller };
        var window = new Window { Content = editor };

        try
        {
            window.Show();
            window.UpdateLayout();
            editor.Focus();
            editor.SelectionStart = 2;
            editor.SelectionEnd = 3;

            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            window.KeyTextInput("Ж例");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\n");
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(controller.Tokens.Any(token => token.Kind == SpardTokenKind.Alias), Is.False);
                Assert.That(controller.Tokens.Last().Kind, Is.EqualTo(SpardTokenKind.Line));
                Assert.That(controller.DisplayText, Is.EqualTo("Ж例\n"));
                Assert.That(editor.Text, Is.EqualTo(controller.DisplayText));
                Assert.That(editor.CaretIndex, Is.EqualTo(controller.CaretOffset));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void FocusLoss_CommitsCanonicalText()
    {
        var controller = new SpardEditorController();
        controller.Load("[ignoresp]before");
        string? committed = null;
        controller.Committed += (_, args) => committed = args.Text;
        var editor = new SpardEditorControl { Controller = controller };
        var nextControl = new Button();
        var window = new Window
        {
            Content = new StackPanel { Children = { editor, nextControl } },
        };

        try
        {
            window.Show();
            editor.Focus();
            controller.SetCaret(controller.DisplayText.Length);
            window.KeyTextInput(" after");
            nextControl.Focus();
            Dispatcher.UIThread.RunJobs();

            Assert.That(committed, Is.EqualTo(controller.SerializedText));
            Assert.That(committed, Does.Contain("after"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void ControllerReplacementAndDetach_DoNotRetainStaleSubscriptions()
    {
        var first = new SpardEditorController();
        first.Load("[ignoresp]first");
        var second = new SpardEditorController();
        second.Load("[ignoresp]second");
        var editor = new SpardEditorControl { Controller = first };
        var window = new Window { Content = editor };

        try
        {
            window.Show();
            editor.Controller = second;
            first.SetCaret(first.DisplayText.Length);
            first.InsertText(" stale");

            Assert.That(editor.Text, Is.EqualTo("second"));

            second.SetCaret(second.DisplayText.Length);
            second.InsertAlias("QText");
            Assert.That(editor.Text, Is.EqualTo("secondQText"));

            editor.Text = "unstructured mutation";
            Assert.That(editor.Text, Is.EqualTo("secondQText"));

            window.Close();
            second.SetCaret(second.DisplayText.Length);
            second.InsertText(" detached");

            Assert.That(editor.Text, Is.EqualTo("secondQText"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task BackgroundControllerNotification_IsMarshalledToUiThread()
    {
        var controller = new SpardEditorController();
        controller.Load("[ignoresp]before");
        var editor = new SpardEditorControl { Controller = controller };
        var window = new Window { Content = editor };

        try
        {
            window.Show();

            await Task.Run(() =>
            {
                controller.SetCaret(controller.DisplayText.Length);
                controller.InsertText(" background");
            });
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.That(editor.Text, Is.EqualTo("before background"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task TokenPresenter_RendersSemanticClassesAliasColorAndAccessibleLabels()
    {
        var controller = new SpardEditorController(name => name == "QText" ? "Question text" : name);
        controller.Load("[ignoresp]Prompt':[m]<QText>(<Line>[ignoresp]optional)?");
        var presenter = new SpardTokenPresenter
        {
            Controller = controller,
            Aliases = [new SpardAliasDescriptor("QText", "Question text", "#FF98FB98")],
        };
        var window = new Window
        {
            Width = 900,
            Height = 180,
            Content = new Border { Padding = new global::Avalonia.Thickness(12), Child = presenter },
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            var tokenBorders = presenter.Children.OfType<Border>().ToArray();
            var alias = tokenBorders.Single(border => border.Classes.Contains("alias"));
            var line = tokenBorders.Single(border => border.Classes.Contains("line"));

            Assert.Multiple(() =>
            {
                Assert.That(tokenBorders, Has.Length.EqualTo(controller.Tokens.Count));
                Assert.That(tokenBorders.Count(border => border.Classes.Contains("optional")), Is.EqualTo(2));
                Assert.That(((SolidColorBrush)alias.BorderBrush!).Color, Is.EqualTo(Color.Parse("#FF98FB98")));
                Assert.That(alias.GetValue(AutomationProperties.NameProperty)?.ToString(),
                    Does.StartWith(UiStrings.SpardAliasToken));
                Assert.That(((TextBlock)line.Child!).Text, Is.EqualTo("›↵"));
                Assert.That(line.GetValue(AutomationProperties.NameProperty)?.ToString(),
                    Does.StartWith(UiStrings.SpardLineToken));
                Assert.That(line.GetValue(AutomationProperties.NameProperty)?.ToString(),
                    Does.Contain($"{UiStrings.SpardTokenDepth}: 1"));
            });

            await Task.Run(() =>
            {
                controller.SetCaret(controller.DisplayText.Length);
                controller.InsertText(" updated");
            });
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.That(presenter.Children, Has.Count.EqualTo(controller.Tokens.Count));

            controller.Load("<Digit>+");
            var opaque = presenter.Children.OfType<Border>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(opaque.Classes.Contains("opaque"), Is.True);
                Assert.That(opaque.GetValue(AutomationProperties.NameProperty)?.ToString(),
                    Does.StartWith(UiStrings.SpardOpaqueToken));
            });

            window.Close();
            controller.InsertAlias("QText");
            Assert.That(presenter.Children, Is.Empty);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void TokenPresenter_BoundsLargeStreamsAndAcceptsUpdatedAliasMetadata()
    {
        var controller = new SpardEditorController();
        controller.Load("[ignoresp]" + string.Concat(Enumerable.Repeat("<QText>", 300)));
        var presenter = new SpardTokenPresenter
        {
            Controller = controller,
            Aliases =
            [
                new SpardAliasDescriptor("QText", "Old label", "#FFFF0000"),
                new SpardAliasDescriptor("QText", "Current label", "#FF008000"),
            ],
        };
        var window = new Window { Content = presenter };

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Multiple(() =>
            {
                Assert.That(controller.Tokens, Has.Count.EqualTo(300));
                Assert.That(presenter.Children, Has.Count.EqualTo(257));
                Assert.That(((SolidColorBrush)((Border)presenter.Children[0]).BorderBrush!).Color,
                    Is.EqualTo(Color.Parse("#FF008000")));
                Assert.That(((Border)presenter.Children[^1]).GetValue(AutomationProperties.NameProperty)?.ToString(),
                    Is.EqualTo(UiStrings.SpardTokenPreviewTruncated));
            });
        }
        finally
        {
            window.Close();
        }
    }
}
