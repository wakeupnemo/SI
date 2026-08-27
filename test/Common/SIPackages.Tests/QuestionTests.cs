using SIPackages.Core;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace SIPackages.Tests;

internal sealed class QuestionTests
{
    [Test]
    public void GetContent_IncludesQuestionAndScriptParameters()
    {
        var questionContent = new ContentItem { Type = ContentTypes.Image, Value = "question.png", IsRef = true };
        var scriptContent = new ContentItem { Type = ContentTypes.Audio, Value = "script.mp3", IsRef = true };
        var question = new Question { Script = new Script() };
        question.Parameters[QuestionParameterNames.Question] = new StepParameter
        {
            Type = StepParameterTypes.Content,
            ContentValue = [questionContent],
        };
        question.Script.Steps.Add(new Step
        {
            Type = StepTypes.ShowContent,
            Parameters =
            {
                [StepParameterNames.Content] = new StepParameter
                {
                    Type = StepParameterTypes.Content,
                    ContentValue = [scriptContent],
                },
            },
        });

        Assert.That(question.GetContent(), Is.EqualTo(new[] { scriptContent, questionContent }));
    }

    [Test]
    public void Clone_PreservesIndependentScriptAndEveryParameterKind()
    {
        var question = new Question
        {
            Script = new Script
            {
                Steps =
                {
                    new Step
                    {
                        Type = "custom",
                        Parameters =
                        {
                            ["simple"] = new StepParameter { SimpleValue = "value" },
                            ["content"] = new StepParameter
                            {
                                Type = StepParameterTypes.Content,
                                ContentValue = new List<ContentItem>
                                {
                                    new() { Type = ContentTypes.Text, Value = "text" },
                                },
                            },
                            ["group"] = new StepParameter
                            {
                                Type = StepParameterTypes.Group,
                                GroupValue = new StepParameters
                                {
                                    ["inner"] = new StepParameter { SimpleValue = "nested" },
                                },
                            },
                            ["numbers"] = new StepParameter
                            {
                                Type = StepParameterTypes.NumberSet,
                                NumberSetValue = new NumberSet { Minimum = 10, Maximum = 50, Step = 10 },
                            },
                        },
                    },
                },
            },
        };

        var clone = question.Clone();
        clone.Script!.Steps[0].Parameters["content"].ContentValue![0].Value = "changed";

        Assert.Multiple(() =>
        {
            Assert.That(clone.Script, Is.Not.SameAs(question.Script));
            Assert.That(clone.Script.Steps, Has.Count.EqualTo(1));
            Assert.That(clone.Script.Steps[0].Type, Is.EqualTo("custom"));
            Assert.That(clone.Script.Steps[0], Is.Not.SameAs(question.Script.Steps[0]));
            Assert.That(question.Script.Steps[0].Parameters["content"].ContentValue![0].Value,
                Is.EqualTo("text"));
            Assert.That(clone.Script.Steps[0].Parameters["group"].GroupValue!["inner"].SimpleValue,
                Is.EqualTo("nested"));
            Assert.That(clone.Script.Steps[0].Parameters["numbers"].NumberSetValue!.Step,
                Is.EqualTo(10));
        });
    }

    [Test]
    public void Serialize_Deserialize_Ok()
    {
        var question = new Question
        {
            Script = new Script
            {
                Steps =
                {
                    new Step
                    {
                        Type = StepTypes.ShowContent,
                        Parameters =
                        {
                            [StepParameterNames.Content] = new StepParameter
                            {
                                Type = StepParameterTypes.Content,
                                ContentValue = new List<ContentItem>
                                {
                                    new() { Type = ContentTypes.Text, Value = "item text" }
                                }
                            }
                        }
                    }
                }
            }
        };

        question.Parameters["test"] = new StepParameter
        {
            Type = StepParameterTypes.Group,
            GroupValue = new StepParameters
            {
                ["inner"] = new StepParameter
                {
                    Type = StepParameterTypes.Simple,
                    SimpleValue = "value"
                }
            }
        };

        var sb = new StringBuilder();

        using (var writer = XmlWriter.Create(sb))
        {
            question.WriteXml(writer);
        }

        var result = sb.ToString();

        var xmlDocument = new XmlDocument();
        xmlDocument.LoadXml(result);

        var itemValue = xmlDocument["question"]?["script"]?["step"]?["param"]?["item"]?.InnerText;
        Assert.That(itemValue, Is.EqualTo("item text"));

        var paramValue = xmlDocument["question"]?["params"]?["param"]?.InnerText;
        Assert.That(paramValue, Is.EqualTo("value"));

        var newQuestion = new Question();

        using (var textReader = new StringReader(result))
        using (var reader = XmlReader.Create(textReader))
        {
            newQuestion.ReadXml(reader);
        }

        var newStep = newQuestion.Script?.Steps[0];

        Assert.Multiple(() =>
        {
            Assert.That(newStep?.Type, Is.EqualTo(StepTypes.ShowContent));
            Assert.That(newStep?.Parameters[StepParameterNames.Content].Type, Is.EqualTo(StepParameterTypes.Content));
            Assert.That(newStep?.Parameters[StepParameterNames.Content].ContentValue?[0].Value, Is.EqualTo("item text"));
        });

        var newParam = newQuestion.Parameters?["test"];

        Assert.Multiple(() =>
        {
            Assert.That(newParam?.Type, Is.EqualTo(StepParameterTypes.Group));
            Assert.That(newParam?.GroupValue?["inner"].Type, Is.EqualTo(StepParameterTypes.Simple));
            Assert.That(newParam?.GroupValue?["inner"].SimpleValue, Is.EqualTo("value"));
        });
    }

    [Test]
    public void GetText_Script_Ok()
    {
        var question = new Question
        {
            Script = new Script
            {
                Steps =
                {
                    new Step
                    {
                        Type = StepTypes.ShowContent,
                        Parameters =
                        {
                            [StepParameterNames.Content] = new StepParameter
                            {
                                Type = StepParameterTypes.Content,
                                ContentValue = new List<ContentItem>
                                {
                                    new() { Type = ContentTypes.Text, Value = "item text" },
                                    new() { Type = ContentTypes.Text, Value = "item text 2" }
                                }
                            }
                        }
                    }
                }
            },
            TypeName = "test"
        };

        var text = question.GetText();

        Assert.That(text, Is.EqualTo("item text\nitem text 2"));
    }

    [Test]
    public void GetText_Parameters_Ok()
    {
        var question = new Question
        {
            TypeName = "test"
        };

        question.Parameters[QuestionParameterNames.Question] = new StepParameter
        {
            Type = StepParameterTypes.Content,
            ContentValue = new List<ContentItem>
            {
                new() { Type = ContentTypes.Text, Value = "item text" },
                new() { Type = ContentTypes.Text, Value = "item text 2" }
            }
        };

        var text = question.GetText();

        Assert.That(text, Is.EqualTo("item text\nitem text 2"));
    }

    [Test]
    public void GetQuestionReportText_Script_WithMediaHash_Ok()
    {
        var stream = new MemoryStream();
        var data = new byte[] { 1, 2, 3, 4 };
        var expectedHash = Convert.ToHexString(SHA256.HashData(data));

        using (var document = SIDocument.Create("Test Package", "Test Author", stream, true))
        {
            using var fileStream = new MemoryStream(data);
            document.Images.AddFileAsync("test.png", fileStream).GetAwaiter().GetResult();
            document.Save();
        }

        stream.Position = 0;

        using var loaded = SIDocument.Load(stream);

        var question = new Question
        {
            Script = new Script
            {
                Steps =
                {
                    new Step
                    {
                        Type = StepTypes.ShowContent,
                        Parameters =
                        {
                            [StepParameterNames.Content] = new StepParameter
                            {
                                Type = StepParameterTypes.Content,
                                ContentValue = new List<ContentItem>
                                {
                                    new() { Type = ContentTypes.Text, Value = "item text" },
                                    new() { Type = ContentTypes.Image, Value = "test.png", IsRef = true },
                                    new() { Type = ContentTypes.Text, Value = "item text 2" }
                                }
                            }
                        }
                    }
                }
            },
            TypeName = "test"
        };

        var text = loaded.GetQuestionReportText(question);

        Assert.That(text, Is.EqualTo($"item text\nimage:{expectedHash}\nitem text 2"));
    }

    [Test]
    public void GetQuestionReportText_Parameters_UsesEmptyStringForMissingHash()
    {
        using var document = SIDocument.Create("Test Package", "Test Author");

        var question = new Question
        {
            TypeName = "test"
        };

        question.Parameters[QuestionParameterNames.Question] = new StepParameter
        {
            Type = StepParameterTypes.Content,
            ContentValue = new List<ContentItem>
            {
                new() { Type = ContentTypes.Text, Value = "item text" },
                new() { Type = ContentTypes.Image, Value = "missing.png", IsRef = true },
                new() { Type = ContentTypes.Text, Value = "item text 2" }
            }
        };

        var text = document.GetQuestionReportText(question);

        Assert.That(text, Is.EqualTo("item text\n\nitem text 2"));
    }
}
