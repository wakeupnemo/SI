using System.Text.Json;

namespace SIQuester.ViewModel.Workspaces.Dialogs.Play;

/// <summary>
/// Defines message names understood by the retained application-owned question player.
/// </summary>
public static class QuestionPreviewMessageTypes
{
    public const string EndPressButtonByTimeout = "endPressButtonByTimeout";
    public const string Content = "content";
    public const string RightAnswer = "rightAnswer";
    public const string Replic = "replic";
    public const string BeginPressButton = "beginPressButton";
    public const string SetReadingSpeed = "setReadingSpeed";
    public const string RightAnswerStart = "rightAnswerStart";
    public const string AnswerOptionsLayout = "answerOptionsLayout";
    public const string AnswerOption = "answerOption";
    public const string ContentState = "contentState";
}

/// <summary>Base type for .NET-to-player protocol messages.</summary>
/// <param name="Type">Protocol message type.</param>
public abstract record QuestionPreviewMessage(string Type);

/// <summary>Defines a protocol message with no payload.</summary>
public sealed record QuestionPreviewSignalMessage : QuestionPreviewMessage
{
    public QuestionPreviewSignalMessage(string type) : base(ValidateType(type)) { }

    private static string ValidateType(string type) => type switch
    {
        QuestionPreviewMessageTypes.EndPressButtonByTimeout => type,
        QuestionPreviewMessageTypes.BeginPressButton => type,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported signal message type."),
    };
}

/// <summary>Defines one media or text item in a content message.</summary>
public sealed record QuestionPreviewContentItem(string Type, string Value);

/// <summary>Replaces content at a player placement.</summary>
public sealed record QuestionPreviewContentMessage(
    string Placement,
    IReadOnlyList<QuestionPreviewContentItem> Content)
    : QuestionPreviewMessage(QuestionPreviewMessageTypes.Content);

/// <summary>Shows a showman/player replica.</summary>
public sealed record QuestionPreviewReplicMessage(string PersonCode, string Text)
    : QuestionPreviewMessage(QuestionPreviewMessageTypes.Replic);

/// <summary>Shows a simple right answer.</summary>
public sealed record QuestionPreviewRightAnswerMessage(string Answer)
    : QuestionPreviewMessage(QuestionPreviewMessageTypes.RightAnswer);

/// <summary>Starts presentation of a structured right answer.</summary>
public sealed record QuestionPreviewRightAnswerStartMessage(string Answer)
    : QuestionPreviewMessage(QuestionPreviewMessageTypes.RightAnswerStart);

/// <summary>Sets the retained player's text reading speed.</summary>
public sealed record QuestionPreviewReadingSpeedMessage(int ReadingSpeed)
    : QuestionPreviewMessage(QuestionPreviewMessageTypes.SetReadingSpeed);

/// <summary>Configures the answer-options layout.</summary>
public sealed record QuestionPreviewAnswerOptionsLayoutMessage(
    bool QuestionHasScreenContent,
    IReadOnlyList<string> TypeNames)
    : QuestionPreviewMessage(QuestionPreviewMessageTypes.AnswerOptionsLayout);

/// <summary>Adds one answer option to the configured layout.</summary>
public sealed record QuestionPreviewAnswerOptionMessage(
    int Index,
    string Label,
    string ContentType,
    string ContentValue)
    : QuestionPreviewMessage(QuestionPreviewMessageTypes.AnswerOption);

/// <summary>Changes the state of one rendered content item.</summary>
public sealed record QuestionPreviewContentStateMessage(
    string Placement,
    int LayoutId,
    int ItemState)
    : QuestionPreviewMessage(QuestionPreviewMessageTypes.ContentState);

/// <summary>
/// Serializes the versioned-in-code retained question-player protocol independently of a native WebView.
/// </summary>
public static class QuestionPreviewProtocol
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes a typed outbound protocol message using the existing camel-case wire shape.</summary>
    public static string Serialize(QuestionPreviewMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.Serialize(message, message.GetType(), SerializerOptions);
    }
}
