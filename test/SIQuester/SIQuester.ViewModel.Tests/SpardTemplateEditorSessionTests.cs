using SIQuester.Model;
using SIQuester.ViewModel.Services;
using SIQuester.ViewModel.Tests.Mocks;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class SpardTemplateEditorSessionTests
{
    [Test]
    public void TemplateAndStructuralEditor_StaySynchronizedThroughExistingCommands()
    {
        var template = CreateTemplate();
        template.Transform = "[ignoresp]start";
        using var session = new SpardTemplateEditorSession(template);

        Assert.Multiple(() =>
        {
            Assert.That(session.Editor.DisplayText, Is.EqualTo("start"));
            Assert.That(session.Aliases,
                Is.EqualTo(new[] { new SpardAliasDescriptor("QText", "Question text", "#FF98FB98") }));
        });

        session.Editor.SetCaret(session.Editor.DisplayText.Length);
        template.InsertAlias.Execute("QText");
        template.InsertOptional.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(session.Editor.DisplayText, Is.EqualTo("startQuestion text()?"));
            Assert.That(template.Transform, Is.EqualTo(session.Editor.SerializedText));
        });

        template.ChangeTemplate.Execute("[m]<QText>[ignoresp]changed");

        Assert.Multiple(() =>
        {
            Assert.That(session.Editor.DisplayText, Is.EqualTo("Question textchanged"));
            Assert.That(session.Editor.CaretOffset, Is.Zero);
        });

        session.Editor.SetCaret(session.Editor.DisplayText.Length);
        session.Editor.InsertText(" 例");

        Assert.That(template.Transform, Is.EqualTo(session.Editor.SerializedText));
        Assert.That(template.Transform, Does.Contain("例"));
    }

    [Test]
    public void Dispose_DetachesBothSynchronizationDirectionsAndTemplateActions()
    {
        var template = CreateTemplate();
        template.Transform = "[ignoresp]before";
        var session = new SpardTemplateEditorSession(template);
        var editor = session.Editor;

        session.Dispose();
        session.Dispose();
        template.Transform = "[ignoresp]external";
        template.InsertAlias.Execute("QText");

        Assert.That(editor.DisplayText, Is.EqualTo("before"));

        editor.SetCaret(editor.DisplayText.Length);
        editor.InsertText(" local");

        Assert.That(template.Transform, Is.EqualTo("[ignoresp]external"));
    }

    private static SpardTemplateViewModel CreateTemplate()
    {
        var template = new SpardTemplateViewModel("Question", new ClipboardServiceMock(), _ => { });
        template.Aliases["QText"] = new EditAlias("Question text", "#FF98FB98");
        return template;
    }
}
