using SIPackages.Core;
using System.Text;
using System.Xml;

namespace SIPackages.Tests;

/// <summary>
/// Tests for Script-based questions deserialization from XML.
/// </summary>
public sealed class ScriptDeserializationTests
{
    [Test]
    public void ReadXml_SelfClosingParameters_PreservesNamesTypesAndEmptyValues()
    {
        const string xml = """
            <params>
              <param name="simple" />
              <param name="content" type="content" />
              <param name="group" type="group" />
              <param name="reference" isRef="True" />
              <param name="future" type="future-kind" />
            </params>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        using var reader = XmlReader.Create(stream);
        reader.MoveToContent();
        var parameters = new StepParameters();

        parameters.ReadXml(reader, limits: null);

        Assert.Multiple(() =>
        {
            Assert.That(parameters, Has.Count.EqualTo(5));
            Assert.That(parameters["simple"].SimpleValue, Is.Empty);
            Assert.That(parameters["content"].ContentValue, Is.Empty);
            Assert.That(parameters["group"].GroupValue, Is.Empty);
            Assert.That(parameters["reference"].IsRef, Is.True);
            Assert.That(parameters["reference"].SimpleValue, Is.Empty);
            Assert.That(parameters["future"].Type, Is.EqualTo("future-kind"));
            Assert.That(parameters["future"].SimpleValue, Is.Empty);
        });
    }

    [Test]
    public void LoadXml_ScriptQuestion_IsDeserialized()
    {
        // Arrange & Act
        using var xmlStream = File.OpenRead("ScriptTest.xml");
        using var document = SIDocument.LoadXml(xmlStream);

        // Assert
        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        Assert.That(question.Script, Is.Not.Null);
        Assert.That(question.Script.Steps, Has.Count.EqualTo(2));
    }

    [Test]
    public void LoadXml_ScriptQuestion_ShowContentStep_IsDeserialized()
    {
        // Arrange & Act
        using var xmlStream = File.OpenRead("ScriptTest.xml");
        using var document = SIDocument.LoadXml(xmlStream);

        // Assert
        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        var showContentStep = question.Script!.Steps[0];
        
        Assert.Multiple(() =>
        {
            Assert.That(showContentStep.Type, Is.EqualTo(StepTypes.ShowContent));
            Assert.That(showContentStep.Parameters.ContainsKey(StepParameterNames.Content), Is.True);
        });

        var contentParam = showContentStep.Parameters[StepParameterNames.Content];
        Assert.That(contentParam.Type, Is.EqualTo(StepParameterTypes.Content));
        Assert.That(contentParam.ContentValue, Has.Count.EqualTo(1));
        Assert.That(contentParam.ContentValue![0].Value, Is.EqualTo("Question text in script"));
    }

    [Test]
    public void LoadXml_ScriptQuestion_AcceptStep_IsDeserialized()
    {
        // Arrange & Act
        using var xmlStream = File.OpenRead("ScriptTest.xml");
        using var document = SIDocument.LoadXml(xmlStream);

        // Assert
        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        var acceptStep = question.Script!.Steps[1];
        
        Assert.Multiple(() =>
        {
            Assert.That(acceptStep.Type, Is.EqualTo(StepTypes.Accept));
            Assert.That(acceptStep.Parameters.ContainsKey("answer"), Is.True);
        });

        var answerParam = acceptStep.Parameters["answer"];
        Assert.That(answerParam.Type, Is.EqualTo(StepParameterTypes.Content));
        Assert.That(answerParam.ContentValue![0].Value, Is.EqualTo("Answer in script"));
    }

    [Test]
    public void LoadXml_MultiStepScriptQuestion_IsDeserialized()
    {
        // Arrange & Act
        using var xmlStream = File.OpenRead("ScriptTest.xml");
        using var document = SIDocument.LoadXml(xmlStream);

        // Assert
        var question = document.Package.Rounds[0].Themes[0].Questions[1];
        Assert.That(question.Script, Is.Not.Null);
        Assert.That(question.Script.Steps, Has.Count.EqualTo(2));
        
        var firstStep = question.Script.Steps[0];
        Assert.That(firstStep.Parameters[StepParameterNames.Content].ContentValue, Has.Count.EqualTo(2));
        
        var secondStep = question.Script.Steps[1];
        Assert.That(secondStep.Parameters[StepParameterNames.Content].ContentValue, Has.Count.EqualTo(1));
    }

    [Test]
    public void LoadXml_ScriptQuestion_RightAnswers_AreDeserialized()
    {
        // Arrange & Act
        using var xmlStream = File.OpenRead("ScriptTest.xml");
        using var document = SIDocument.LoadXml(xmlStream);

        // Assert
        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        Assert.That(question.Right, Has.Count.EqualTo(1));
        Assert.That(question.Right[0], Is.EqualTo("Correct answer"));
    }
}
