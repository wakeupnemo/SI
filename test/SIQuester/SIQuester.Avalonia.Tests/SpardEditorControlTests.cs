using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NUnit.Framework;
using SIQuester.Avalonia.Controls;
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
}
