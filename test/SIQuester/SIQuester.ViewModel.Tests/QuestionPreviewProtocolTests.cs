using Microsoft.Extensions.DependencyInjection;
using SIEngine.Core;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;
using SIQuester.ViewModel.Workspaces.Dialogs;
using SIQuester.ViewModel.Workspaces.Dialogs.Play;
using System.Text.Json;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class QuestionPreviewProtocolTests
{
    [Test]
    public void TypedMessages_SerializeExactLegacyCamelCaseShape()
    {
        var signal = Parse(QuestionPreviewProtocol.Serialize(
            new QuestionPreviewSignalMessage(QuestionPreviewMessageTypes.BeginPressButton)));
        var content = Parse(QuestionPreviewProtocol.Serialize(new QuestionPreviewContentMessage(
            "screen",
            [new QuestionPreviewContentItem("text", "Текст 例")])));
        var answerOption = Parse(QuestionPreviewProtocol.Serialize(new QuestionPreviewAnswerOptionMessage(
            2,
            "Б",
            "image",
            "media/изображение 例.png")));
        var contentState = Parse(QuestionPreviewProtocol.Serialize(new QuestionPreviewContentStateMessage(
            "screen",
            3,
            2)));
        var remainingMessages = new (QuestionPreviewMessage Message, string Type, string[] Fields)[]
        {
            (new QuestionPreviewReplicMessage("s", "Реплика"), "replic", ["type", "personCode", "text"]),
            (new QuestionPreviewRightAnswerMessage("Ответ"), "rightAnswer", ["type", "answer"]),
            (new QuestionPreviewRightAnswerStartMessage("Ответ"), "rightAnswerStart", ["type", "answer"]),
            (new QuestionPreviewReadingSpeedMessage(0), "setReadingSpeed", ["type", "readingSpeed"]),
            (new QuestionPreviewAnswerOptionsLayoutMessage(true, ["text", "image"]),
                "answerOptionsLayout",
                ["type", "questionHasScreenContent", "typeNames"]),
        };

        Assert.Multiple(() =>
        {
            Assert.That(signal.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "type" }));
            Assert.That(signal.GetProperty("type").GetString(),
                Is.EqualTo(QuestionPreviewMessageTypes.BeginPressButton));

            Assert.That(content.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "type", "placement", "content" }));
            Assert.That(content.GetProperty("type").GetString(), Is.EqualTo("content"));
            Assert.That(content.GetProperty("placement").GetString(), Is.EqualTo("screen"));
            Assert.That(content.GetProperty("content")[0].GetProperty("type").GetString(), Is.EqualTo("text"));
            Assert.That(content.GetProperty("content")[0].GetProperty("value").GetString(), Is.EqualTo("Текст 例"));

            Assert.That(answerOption.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "type", "index", "label", "contentType", "contentValue" }));
            Assert.That(answerOption.GetProperty("index").GetInt32(), Is.EqualTo(2));
            Assert.That(answerOption.GetProperty("label").GetString(), Is.EqualTo("Б"));
            Assert.That(answerOption.GetProperty("contentType").GetString(), Is.EqualTo("image"));
            Assert.That(answerOption.GetProperty("contentValue").GetString(),
                Is.EqualTo("media/изображение 例.png"));

            Assert.That(contentState.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "type", "placement", "layoutId", "itemState" }));
            Assert.That(contentState.GetProperty("layoutId").GetInt32(), Is.EqualTo(3));
            Assert.That(contentState.GetProperty("itemState").GetInt32(), Is.EqualTo(2));

            foreach (var (message, type, fields) in remainingMessages)
            {
                var json = Parse(QuestionPreviewProtocol.Serialize(message));
                Assert.That(json.EnumerateObject().Select(property => property.Name),
                    Is.EquivalentTo(fields),
                    type);
                Assert.That(json.GetProperty("type").GetString(), Is.EqualTo(type));
            }
        });
    }

    [Test]
    public void HostDescriptor_RequiresAbsoluteSourcesAndHidesUnavailableSources()
    {
        var source = new Uri("http://127.0.0.1:52731/index.html");
        var available = QuestionPreviewHostDescriptor.Available(source);
        var unavailable = QuestionPreviewHostDescriptor.Unavailable(
            QuestionPreviewAvailability.BackendUnavailable,
            QuestionPreviewBackendRequirement.LinuxWebKit);

        Assert.Multiple(() =>
        {
            Assert.That(available.IsAvailable, Is.True);
            Assert.That(available.ApplicationSource, Is.EqualTo(source));
            Assert.That(unavailable.IsAvailable, Is.False);
            Assert.That(unavailable.ApplicationSource, Is.Null);
            Assert.That(unavailable.BackendRequirement,
                Is.EqualTo(QuestionPreviewBackendRequirement.LinuxWebKit));
            Assert.Throws<ArgumentException>(() =>
                QuestionPreviewHostDescriptor.Available(new Uri("relative/index.html", UriKind.Relative)));
            Assert.Throws<ArgumentException>(() =>
                QuestionPreviewHostDescriptor.Available(new Uri("javascript:alert(1)")));
            Assert.Throws<ArgumentException>(() =>
                QuestionPreviewHostDescriptor.Available(new Uri("https://preview.siquester.invalid/index.html")));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                QuestionPreviewHostDescriptor.Unavailable(QuestionPreviewAvailability.Available));
            Assert.Throws<ArgumentException>(() => QuestionPreviewHostDescriptor.Unavailable(
                QuestionPreviewAvailability.AssetsUnavailable,
                QuestionPreviewBackendRequirement.LinuxWebKit));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuestionPreviewSignalMessage("unknown"));
        });
    }

    [Test]
    public void NativeHostReadiness_GatesPlaybackAndFailureIsTerminal()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var document = TestHelper.CreateDocumentViewModelFactory(serviceProvider)
            .CreateViewModelFor(TestHelper.CreateSimpleTestPackage());
        var preview = new QuestionPlayViewModel(
            document.Package.Rounds[0].Themes[0].Questions[0],
            document,
            new AvailablePreviewService());

        preview.SetPreviewReady(false);
        var canPlayWhileLoading = preview.Play.CanExecute(null);
        preview.SetPreviewReady(true);
        var canPlayWhenReady = preview.Play.CanExecute(null);
        preview.ReportPreviewHostFailure(new InvalidOperationException("native host stopped"));
        preview.SetPreviewReady(true);

        Assert.Multiple(() =>
        {
            Assert.That(canPlayWhileLoading, Is.False);
            Assert.That(canPlayWhenReady, Is.True);
            Assert.That(preview.HasPreviewFailure, Is.True);
            Assert.That(preview.IsPreviewLoading, Is.False);
            Assert.That(preview.Play.CanExecute(null), Is.False);
            Assert.That(preview.Replay.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void EngineCallbacks_EmitTypedProtocolWithoutNativeWebView()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var document = TestHelper.CreateDocumentViewModelFactory(serviceProvider)
            .CreateViewModelFor(TestHelper.CreateSimpleTestPackage());
        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        var preview = new QuestionPlayViewModel(
            question,
            document,
            new AvailablePreviewService());
        var messages = new List<JsonElement>();
        preview.SendJsonMessage += message => messages.Add(Parse(message));

        preview.OnQuestionStart(false, ["Ответ 例"], () => { });
        preview.OnQuestionContent(
            [
                new ContentItem { Placement = ContentPlacements.Replic, Type = ContentTypes.Text, Value = "Реплика" },
                new ContentItem { Placement = ContentPlacements.Screen, Type = ContentTypes.Text, Value = "Вопрос 例" },
                new ContentItem { Placement = ContentPlacements.Screen, Type = ContentTypes.Image, Value = "https://example.invalid/a.png" },
                new ContentItem { Placement = ContentPlacements.Background, Type = ContentTypes.Audio, Value = "https://example.invalid/a.ogg" },
            ],
            false);
        preview.OnAskAnswer(StepParameterValues.AskAnswerMode_Button, 5000);
        preview.OnAnswerStart();
        preview.OnContentStart([], _ => { });
        preview.OnSimpleRightAnswerStart();
        preview.OnQuestionContent(
            [new ContentItem { Placement = ContentPlacements.Screen, Type = ContentTypes.Text, Value = "Ответ 例" }],
            true);
        preview.OnAnswerOptions(
            [
                new AnswerOption("А", new ContentItem { Type = ContentTypes.Text, Value = "Первый" }),
                new AnswerOption("Б", new ContentItem { Type = ContentTypes.Image, Value = "https://example.invalid/b.png" }),
            ],
            []);
        preview.OnRightAnswerOption("Б");
        var callbackMessages = messages.ToArray();
        messages.Clear();
        preview.Replay.Execute(null);
        var replayMessages = messages.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(preview.IsPreviewAvailable, Is.True);
            Assert.That(preview.Play.CanExecute(null), Is.True);
            Assert.That(callbackMessages.Select(message => message.GetProperty("type").GetString()),
                Is.EqualTo(new[]
                {
                    "setReadingSpeed",
                    "replic",
                    "content",
                    "content",
                    "beginPressButton",
                    "replic",
                    "rightAnswerStart",
                    "rightAnswer",
                    "content",
                    "answerOptionsLayout",
                    "answerOption",
                    "answerOption",
                    "contentState",
                }));
            Assert.That(callbackMessages[2].GetProperty("content")[0].GetProperty("value").GetString(),
                Is.EqualTo("https://example.invalid/a.ogg"));
            Assert.That(callbackMessages[3].GetProperty("content")[0].GetProperty("value").GetString(),
                Is.EqualTo("Вопрос 例"));
            Assert.That(callbackMessages[7].GetProperty("answer").GetString(), Is.EqualTo("Ответ 例"));
            Assert.That(callbackMessages[^1].GetProperty("layoutId").GetInt32(), Is.EqualTo(2));
            Assert.That(replayMessages[0].GetProperty("type").GetString(), Is.EqualTo("content"));
            Assert.That(replayMessages[0].GetProperty("placement").GetString(), Is.EqualTo("screen"));
            Assert.That(replayMessages[0].GetProperty("content").GetArrayLength(), Is.Zero);
            Assert.That(replayMessages[1].GetProperty("type").GetString(),
                Is.EqualTo("endPressButtonByTimeout"));
        });
    }

    [Test]
    public async Task DocumentPreview_UsesSafeUnavailableDefaultAndIgnoresClosedReplacedDialog()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var document = TestHelper.CreateDocumentViewModelFactory(serviceProvider)
            .CreateViewModelFor(TestHelper.CreateSimpleTestPackage());
        var question = document.Package.Rounds[0].Themes[0].Questions[0];
        document.ActiveNode = question;
        document.PlayQuestion.Execute(question);
        var firstPreview = (QuestionPlayViewModel)document.Dialog!;

        document.PlayQuestion.Execute(question);
        var secondPreview = (QuestionPlayViewModel)document.Dialog!;
        await firstPreview.Close.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(document.CanPreviewActiveQuestion, Is.True);
            Assert.That(firstPreview.IsPreviewBackendUnavailable, Is.True);
            Assert.That(firstPreview.Source, Is.Null);
            Assert.That(firstPreview.Play.CanExecute(null), Is.False);
            Assert.That(document.Dialog, Is.SameAs(secondPreview));
        });

        await secondPreview.Close.ExecuteAsync(null);
        Assert.That(document.Dialog, Is.Null);

        document.PlayQuestion.Execute(question);
        Assert.That(document.Dialog, Is.TypeOf<QuestionPlayViewModel>());
        document.Dispose();
        Assert.That(document.Dialog, Is.Null);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class AvailablePreviewService : IQuestionPreviewService
    {
        public QuestionPreviewHostDescriptor GetHostDescriptor() => QuestionPreviewHostDescriptor.Available(
            new Uri("http://127.0.0.1:52731/index.html"));
    }
}
