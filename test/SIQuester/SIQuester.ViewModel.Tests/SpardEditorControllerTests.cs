using SIQuester.ViewModel.Services;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class SpardEditorControllerTests
{
    [Test]
    public void ParseAndSerialize_RoundTripsNestedCanonicalStructureAndMapsTokens()
    {
        const string source = "[ignoresp]Тема':[m]<TName>(<Line>[ignoresp]Автор':[m]<TAuthor>)?";
        var aliases = new Dictionary<string, string>
        {
            ["TName"] = "Theme name",
            ["TAuthor"] = "Theme author",
        };
        var controller = new SpardEditorController(name => aliases.GetValueOrDefault(name, name));

        controller.Load(source);

        Assert.Multiple(() =>
        {
            Assert.That(controller.HasParseError, Is.False);
            Assert.That(controller.SerializedText,
                Is.EqualTo("[on,ignoresp](Тема':)[on,m]<TName>(<Line>[on,ignoresp](Автор':)[on,m]<TAuthor>)?"));
            Assert.That(controller.Tokens.Select(token => token.Kind), Is.EqualTo(new[]
            {
                SpardTokenKind.Text,
                SpardTokenKind.Alias,
                SpardTokenKind.OptionalStart,
                SpardTokenKind.Line,
                SpardTokenKind.Text,
                SpardTokenKind.Alias,
                SpardTokenKind.OptionalEnd,
            }));
            Assert.That(controller.Tokens.Select(token => token.ExpressionPath), Is.Unique);
            Assert.That(controller.DisplayText, Is.EqualTo("Тема:Theme name(\nАвтор:Theme author)?"));
        });

        var reparsed = new SpardEditorController();
        reparsed.Load(controller.SerializedText);
        Assert.That(reparsed.HasParseError, Is.False);
        Assert.That(reparsed.SerializedText, Is.EqualTo(controller.SerializedText));
    }

    [Test]
    public void PlainTextAliasAndLineInsertion_ProduceCanonicalTokensAtCaret()
    {
        var controller = new SpardEditorController();
        controller.Load("[ignoresp]abc");
        controller.SetCaret(1);
        controller.InsertText("Ж例");
        controller.SetCaret(controller.DisplayText.Length);
        controller.InsertAlias("QText");
        controller.InsertLine();

        Assert.Multiple(() =>
        {
            Assert.That(controller.DisplayText, Is.EqualTo("aЖ例bcQText\n"));
            Assert.That(controller.Tokens.Select(token => token.Kind), Is.EqualTo(new[]
            {
                SpardTokenKind.Text,
                SpardTokenKind.Alias,
                SpardTokenKind.Line,
            }));
            Assert.That(controller.SerializedText, Is.EqualTo("[on,ignoresp](aЖ例bc)[m]<QText><Line>"));
            Assert.That(controller.CaretOffset, Is.EqualTo(controller.DisplayText.Length));
        });
    }

    [Test]
    public void OptionalInsertion_WrapsSelectionAndSupportsNestedOptionalGroups()
    {
        var controller = new SpardEditorController();
        controller.Load("[ignoresp]abcdef");
        controller.SetSelection(1, 3);
        controller.InsertOptional();

        Assert.Multiple(() =>
        {
            Assert.That(controller.DisplayText, Is.EqualTo("a(bcd)?ef"));
            Assert.That(controller.SerializedText,
                Is.EqualTo("[ignoresp]a([ignoresp](bcd))?[ignoresp](ef)"));
        });

        var selectedText = controller.Tokens.Single(token => token.Kind == SpardTokenKind.Text && token.Text == "bcd");
        controller.SetSelection(selectedText.Start, selectedText.Length);
        controller.InsertOptional();

        Assert.Multiple(() =>
        {
            Assert.That(controller.DisplayText, Is.EqualTo("a((bcd)?)?ef"));
            Assert.That(controller.Tokens.Count(token => token.Kind == SpardTokenKind.OptionalStart), Is.EqualTo(2));
            Assert.That(controller.Tokens.Count(token => token.Kind == SpardTokenKind.OptionalEnd), Is.EqualTo(2));
            Assert.That(controller.Tokens.Max(token => token.Depth), Is.EqualTo(2));
        });

        var reparsed = new SpardEditorController();
        reparsed.Load(controller.SerializedText);
        Assert.That(reparsed.HasParseError, Is.False);
        Assert.That(reparsed.DisplayText, Is.EqualTo(controller.DisplayText));
    }

    [Test]
    public void BackwardAndForwardDeletion_RemoveCharactersAndWholeStructuralTokens()
    {
        var controller = new SpardEditorController();
        controller.Load("[ignoresp]ab[m]<QText><Line>[ignoresp]cd");
        var alias = controller.Tokens.Single(token => token.Kind == SpardTokenKind.Alias);
        controller.SetCaret(alias.End);

        Assert.That(controller.DeleteBackward(), Is.True);
        Assert.That(controller.Tokens.Any(token => token.Kind == SpardTokenKind.Alias), Is.False);

        var line = controller.Tokens.Single(token => token.Kind == SpardTokenKind.Line);
        controller.SetCaret(line.Start);
        Assert.That(controller.DeleteForward(), Is.True);
        Assert.That(controller.Tokens.Any(token => token.Kind == SpardTokenKind.Line), Is.False);

        controller.SetCaret(2);
        Assert.That(controller.DeleteBackward(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(controller.DisplayText, Is.EqualTo("acd"));
            Assert.That(controller.SerializedText, Is.EqualTo("[on,ignoresp](acd)"));
        });
    }

    [Test]
    public void EmptyOptionalAndSomeAlias_UseLegacyStructuralForms()
    {
        var controller = new SpardEditorController();
        controller.Load("");
        controller.InsertOptional();

        Assert.Multiple(() =>
        {
            Assert.That(controller.DisplayText, Is.EqualTo("()?"));
            Assert.That(controller.CaretOffset, Is.EqualTo(1));
            Assert.That(controller.Tokens.Select(token => token.Kind), Is.EqualTo(new[]
            {
                SpardTokenKind.OptionalStart,
                SpardTokenKind.OptionalEnd,
            }));
        });

        controller.InsertAlias("Some");

        Assert.Multiple(() =>
        {
            Assert.That(controller.DisplayText, Is.EqualTo("(Some)?"));
            Assert.That(controller.SerializedText, Is.EqualTo("(<Some>)?"));
            Assert.That(controller.Tokens.Single(token => token.Kind == SpardTokenKind.Alias).Depth,
                Is.EqualTo(1));
        });
    }

    [Test]
    public void StructuralSelection_ExpandsAliasesAndPairedOptionalBoundaries()
    {
        var controller = new SpardEditorController(name => name == "QText" ? "Question text" : name);
        controller.Load("[m]<QText>([ignoresp]optional)?[ignoresp]tail");
        var alias = controller.Tokens.Single(token => token.Kind == SpardTokenKind.Alias);
        controller.SetSelection(alias.Start + 2, 1);
        Assert.That(controller.Selection, Is.EqualTo(new SpardSelection(alias.Start, alias.Length)));

        var opening = controller.Tokens.Single(token => token.Kind == SpardTokenKind.OptionalStart);
        var closing = controller.Tokens.Single(token => token.Kind == SpardTokenKind.OptionalEnd);
        controller.SetSelection(opening.Start, opening.Length);

        Assert.That(controller.Selection,
            Is.EqualTo(new SpardSelection(opening.Start, closing.End - opening.Start)));
    }

    [Test]
    public void StructuralSelection_ClampsExtremeCallerOffsetsWithoutOverflow()
    {
        var controller = new SpardEditorController();
        controller.Load("[ignoresp]abc");

        controller.SetSelection(int.MaxValue, int.MaxValue);

        Assert.That(controller.Selection, Is.EqualTo(new SpardSelection(3, 0)));
    }

    [Test]
    public void CaretMovement_SkipsAtomicAliasesAndOptionalMarkers()
    {
        var controller = new SpardEditorController(name => name == "QText" ? "Question" : name);
        controller.Load("[m]<QText>([ignoresp]x)?");
        var alias = controller.Tokens.Single(token => token.Kind == SpardTokenKind.Alias);
        controller.SetCaret(alias.Start);

        Assert.That(controller.MoveCaretForward(), Is.True);
        Assert.That(controller.CaretOffset, Is.EqualTo(alias.End));
        Assert.That(controller.MoveCaretBackward(), Is.True);
        Assert.That(controller.CaretOffset, Is.EqualTo(alias.Start));

        var closing = controller.Tokens.Single(token => token.Kind == SpardTokenKind.OptionalEnd);
        controller.SetCaret(closing.Start);
        Assert.That(controller.MoveCaretForward(), Is.True);
        Assert.That(controller.CaretOffset, Is.EqualTo(closing.End));
        Assert.That(controller.MoveCaretBackward(), Is.True);
        Assert.That(controller.CaretOffset, Is.EqualTo(closing.Start));
    }

    [Test]
    public void Commit_PublishesCurrentCanonicalTextForFocusLossHost()
    {
        var controller = new SpardEditorController();
        string? committed = null;
        controller.Committed += (_, args) => committed = args.Text;
        controller.Load("[ignoresp]before");
        controller.SetCaret(controller.DisplayText.Length);
        controller.InsertAlias("Answer");

        controller.Commit();

        Assert.That(committed, Is.EqualTo(controller.SerializedText));
    }

    [Test]
    public void MalformedInput_IsPreservedUntilAnEditAndThenRecoversAsLiteralText()
    {
        const string malformed = "([m]<QText>";
        var controller = new SpardEditorController();
        controller.Load(malformed);

        Assert.Multiple(() =>
        {
            Assert.That(controller.HasParseError, Is.True);
            Assert.That(controller.ParseError, Is.Not.Empty);
            Assert.That(controller.DisplayText, Is.EqualTo(malformed));
            Assert.That(controller.SerializedText, Is.EqualTo(malformed));
        });

        controller.SetCaret(controller.DisplayText.Length);
        controller.InsertText(" fixed");

        Assert.Multiple(() =>
        {
            Assert.That(controller.HasParseError, Is.False);
            Assert.That(controller.SerializedText, Is.Not.EqualTo(malformed));
            Assert.That(controller.DisplayText, Is.EqualTo(malformed + " fixed"));
        });

        var reparsed = new SpardEditorController();
        reparsed.Load(controller.SerializedText);
        Assert.That(reparsed.HasParseError, Is.False);
        Assert.That(reparsed.DisplayText, Is.EqualTo(controller.DisplayText));
    }
}
