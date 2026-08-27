namespace SIPackages.Tests;

public sealed class SIDocumentTests
{
    [Test]
    public void SaveAndReload_PreservesMultiStepQuestionScript()
    {
        using var stream = new MemoryStream();

        using (var document = SIDocument.Create("Script package", "Author", stream, true))
        {
            var round = new Round { Name = "Round" };
            var theme = new Theme { Name = "Theme" };
            var question = new Question { Price = 100, Script = new Script() };
            question.Script.Steps.Add(new Step
            {
                Type = SIPackages.Core.StepTypes.ShowContent,
                Parameters =
                {
                    [SIPackages.Core.StepParameterNames.Content] = new StepParameter
                    {
                        Type = SIPackages.Core.StepParameterTypes.Content,
                        ContentValue = new List<ContentItem>
                        {
                            new() { Type = SIPackages.Core.ContentTypes.Text, Value = "Question text 例" },
                        },
                    },
                },
            });
            question.Script.Steps.Add(new Step { Type = SIPackages.Core.StepTypes.Accept });
            question.Right.Add("Answer");
            theme.Questions.Add(question);
            round.Themes.Add(theme);
            document.Package.Rounds.Add(round);
            document.Save();
        }

        stream.Position = 0;
        using var reloaded = SIDocument.Load(stream);
        var reloadedScript = reloaded.Package.Rounds[0].Themes[0].Questions[0].Script;

        Assert.Multiple(() =>
        {
            Assert.That(reloadedScript, Is.Not.Null);
            Assert.That(reloadedScript!.Steps, Has.Count.EqualTo(2));
            Assert.That(reloadedScript.Steps[0].Parameters[SIPackages.Core.StepParameterNames.Content]
                .ContentValue![0].Value, Is.EqualTo("Question text 例"));
            Assert.That(reloadedScript.Steps[1].Type, Is.EqualTo(SIPackages.Core.StepTypes.Accept));
        });
    }

    [Test]
    public void Save_OldFormat_RemovesLegacyAuthorAndSourceFiles()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            File.Copy("SIGameTest.siq", tempFile, overwrite: true);

            int initialAuthorCount;
            int initialSourceCount;

            using (var stream = File.Open(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var document = SIDocument.Load(stream, read: false))
            {
                initialAuthorCount = document.Authors.Count;
                initialSourceCount = document.Sources.Count;

                document.Save();
            }

            using (var archive = new System.IO.Compression.ZipArchive(File.OpenRead(tempFile), System.IO.Compression.ZipArchiveMode.Read))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(archive.GetEntry("Texts/authors.xml"), Is.Null);
                    Assert.That(archive.GetEntry("Texts/sources.xml"), Is.Null);
                });
            }

            using var reloadedStream = File.OpenRead(tempFile);
            using var reloaded = SIDocument.Load(reloadedStream);

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Authors.Count, Is.EqualTo(initialAuthorCount));
                Assert.That(reloaded.Sources.Count, Is.EqualTo(initialSourceCount));
            });
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public void TestMediaFileNames()
    {
        using var fs = File.OpenRead("test.siq");
        var document = SIDocument.Load(fs);

        var images = document.Images.ToArray();
        var video = document.Video.ToArray();

        var atomLinks = document.Package.Rounds[0].Themes[0].Questions
            .Select(q => q.GetContent().First().Value)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(video[0], Is.EqualTo(atomLinks[0]));
            Assert.That(images[0], Is.EqualTo(atomLinks[1]));
        });
    }

    [Test]
    public void TestCollectionFiles()
    {
        using var fs = File.OpenRead("test.siq");
        var document = SIDocument.Load(fs);

        var images = document.Images.ToArray();
        var file = document.Images.GetFile(images[0]);

        Assert.That(file, Is.Not.Null);
    }
}
