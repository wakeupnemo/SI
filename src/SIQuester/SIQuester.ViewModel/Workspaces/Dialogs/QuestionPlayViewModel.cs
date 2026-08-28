using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIEngine.Core;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Properties;
using SIQuester.ViewModel.Workspaces.Dialogs.Play;
using System.Diagnostics.CodeAnalysis;
using Utils.Commands;
using Utils.Web;

namespace SIQuester.ViewModel.Workspaces.Dialogs;

/// <summary>
/// Defines a view model for question player.
/// </summary>
public sealed class QuestionPlayViewModel : WorkspaceViewModel, IQuestionEnginePlayHandler, IWebInterop
{
    private QuestionEngine _questionEngine;
    private readonly QDocument _qDocument;
    private readonly QuestionViewModel _originalQuestion;
    private readonly ILogger<QuestionPlayViewModel> _logger;
    private readonly IQuestionPreviewSession _previewSession;

    private bool _singleAnswerer = true;
    private bool _isFinished;
    private AnswerOptionViewModel[]? _options;

    private bool _isAnswer = false;
    private bool _isAnswerSimple = false;
    private string _rightAnswer = "";
    private bool _isPreviewReady;
    private bool _hasPreviewFailure;
    private bool _isDisposed;

    public override string Header => Resources.QuestionPlay;

    /// <summary>Gets the host-owned application player source when preview is available.</summary>
    public Uri? Source => PreviewHost.ApplicationSource;

    /// <summary>Gets the native host capability captured when this preview was opened.</summary>
    public QuestionPreviewHostDescriptor PreviewHost { get; }

    /// <summary>Gets whether the host can present the application-owned player.</summary>
    public bool IsPreviewAvailable => PreviewHost.IsAvailable;

    /// <summary>Gets whether the host is missing or has not configured a browser backend.</summary>
    public bool IsPreviewBackendUnavailable =>
        PreviewHost.Availability == QuestionPreviewAvailability.BackendUnavailable;

    /// <summary>Gets whether the application-owned player assets are missing.</summary>
    public bool ArePreviewAssetsUnavailable =>
        PreviewHost.Availability == QuestionPreviewAvailability.AssetsUnavailable;

    /// <summary>Gets whether Linux WebKit runtime packages are required.</summary>
    public bool RequiresLinuxWebKit =>
        PreviewHost.BackendRequirement == QuestionPreviewBackendRequirement.LinuxWebKit;

    /// <summary>Gets whether the Windows WebView2 runtime is required.</summary>
    public bool RequiresWindowsWebView2 =>
        PreviewHost.BackendRequirement == QuestionPreviewBackendRequirement.WindowsWebView2;

    /// <summary>Gets whether the current platform has no supported preview backend.</summary>
    public bool IsPreviewPlatformUnsupported =>
        PreviewHost.BackendRequirement == QuestionPreviewBackendRequirement.UnsupportedPlatform;

    /// <summary>Gets whether only generic backend-unavailable guidance can be shown.</summary>
    public bool IsGenericPreviewBackendUnavailable => IsPreviewBackendUnavailable
        && PreviewHost.BackendRequirement == QuestionPreviewBackendRequirement.None;

    /// <summary>Gets whether the preview is waiting for its application-owned page.</summary>
    public bool IsPreviewLoading => IsPreviewAvailable && !_isPreviewReady && !_hasPreviewFailure;

    /// <summary>Gets whether the current native preview host failed after opening.</summary>
    public bool HasPreviewFailure => _hasPreviewFailure;

    public event Action<string>? SendJsonMessage;

    /// <summary>
    /// Plays the next question fragment.
    /// </summary>
    public SimpleCommand Play { get; private set; }

    /// <summary>
    /// Replays the question from the beginning.
    /// </summary>
    public SimpleCommand Replay { get; private set; }

    /// <summary>
    /// Closes the question player dialog.
    /// </summary>
    public IAsyncCommand CloseDialog { get; }

    /// <summary>
    /// Gets a value indicating whether the Replay button should be visible.
    /// </summary>
    public bool IsReplayVisible => _isFinished;

    /// <summary>
    /// Initializes a new instance of <see cref="QuestionPlayViewModel" /> class.
    /// </summary>
    /// <param name="question">Question to play.</param>
    /// <param name="document">Document that holds the question media content.</param>
    public QuestionPlayViewModel(
        QuestionViewModel question,
        QDocument document,
        IQuestionPreviewService questionPreviewService,
        ILogger<QuestionPlayViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(questionPreviewService);
        _originalQuestion = question;
        _qDocument = document;
        _logger = logger ?? NullLogger<QuestionPlayViewModel>.Instance;
        _previewSession = questionPreviewService.CreateSession()
            ?? throw new InvalidOperationException("The question-preview service returned no session.");
        PreviewHost = _previewSession.Host;
        _isPreviewReady = IsPreviewAvailable;

        Play = new SimpleCommand(Play_Executed);
        Replay = new SimpleCommand(Replay_Executed);
        CloseDialog = Close;

        try
        {
            InitializeQuestionEngine();
        }
        catch
        {
            _previewSession.Dispose();
            throw;
        }
    }

    [MemberNotNull(nameof(_questionEngine))]
    private void InitializeQuestionEngine()
    {
        _questionEngine = new QuestionEngine(
            _originalQuestion.Model,
            new QuestionEngineOptions
            {
                FalseStarts = FalseStartMode.Enabled,
                ShowSimpleRightAnswers = true,
                DefaultTypeName = _originalQuestion.OwnerTheme?.OwnerRound?.Model.Type == RoundTypes.Final ? QuestionTypes.StakeAll : QuestionTypes.Simple
            },
            this);

        _isFinished = false;
        _options = null;
        RefreshPlaybackCommandState();
        
        // Notify visibility changes
        OnPropertyChanged(nameof(IsReplayVisible));
    }

    /// <summary>Updates readiness after the native host loads or detaches its controlled page.</summary>
    public void SetPreviewReady(bool isReady)
    {
        var normalizedReady = IsPreviewAvailable && isReady && !_hasPreviewFailure;

        if (_isPreviewReady == normalizedReady)
        {
            return;
        }

        _isPreviewReady = normalizedReady;
        OnPropertyChanged(nameof(IsPreviewLoading));
        RefreshPlaybackCommandState();
    }

    /// <summary>Records a recoverable native host failure without exposing exception details in the UI.</summary>
    public void ReportPreviewHostFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _logger.LogWarning(exception, "Question preview host failed");

        if (_hasPreviewFailure)
        {
            return;
        }

        _hasPreviewFailure = true;
        _isPreviewReady = false;
        OnPropertyChanged(nameof(HasPreviewFailure));
        OnPropertyChanged(nameof(IsPreviewLoading));
        RefreshPlaybackCommandState();
    }

    /// <summary>Records that the embedded player received the trusted gesture required for audio playback.</summary>
    public void ReportAudioUnlocked() => _logger.LogInformation("Question preview audio unlocked");

    private void RefreshPlaybackCommandState()
    {
        var canPlay = IsPreviewAvailable && _isPreviewReady && !_hasPreviewFailure;
        Play.CanBeExecuted = canPlay && !_isFinished;
        Replay.CanBeExecuted = canPlay;
    }

    /// <summary>
    /// Plays the next question fragment.
    /// </summary>
    public void Play_Executed(object? arg)
    {
        try
        {
            if (_isFinished)
            {
                return;
            }

            if (!_questionEngine.CanNext)
            {
                _isFinished = true;
                Play.CanBeExecuted = false;
                
                // Notify visibility changes when question finishes
                OnPropertyChanged(nameof(IsReplayVisible));
                return;
            }

            OnMessage(new QuestionPreviewSignalMessage(QuestionPreviewMessageTypes.EndPressButtonByTimeout));

            var hasNextFragment = _questionEngine.PlayNext();
            _isFinished = !hasNextFragment || !_questionEngine.CanNext;
            
            // Check if question finished after playing
            if (_isFinished)
            {
                Play.CanBeExecuted = false;
                OnPropertyChanged(nameof(IsReplayVisible));
            }
        }
        catch (Exception exc)
        {
            OnError(exc);
        }
    }

    private void Replay_Executed(object? arg)
    {
        try
        {
            _logger.LogInformation("Question preview replay started");

            // Reset the question engine to start from the beginning
            InitializeQuestionEngine();

            // Reset UI state
            _singleAnswerer = true;

            OnMessage(new QuestionPreviewContentMessage("screen", Array.Empty<QuestionPreviewContentItem>()));

            Play.Execute(null);
        }
        catch (Exception exc)
        {
            OnError(exc);
        }
    }

    public void OnQuestionContent(IReadOnlyCollection<ContentItem> content, bool isLast)
    {
        var screenContent = new List<QuestionPreviewContentItem>();

        foreach (var contentItem in content)
        {
            switch (contentItem.Placement)
            {
                case ContentPlacements.Replic:
                    OnMessage(new QuestionPreviewReplicMessage("s", contentItem.Value));
                    break;

                case ContentPlacements.Screen:
                    switch (contentItem.Type)
                    {
                        case ContentTypes.Text:
                            screenContent.Add(new QuestionPreviewContentItem("text", contentItem.Value));
                            break;

                        case ContentTypes.Image:
                            screenContent.Add(new QuestionPreviewContentItem(
                                "image",
                                contentItem.IsRef
                                    ? ResolvePackageMedia(
                                        _qDocument.Images,
                                        QuestionPreviewMediaKind.Image,
                                        contentItem.Value)
                                    : contentItem.Value));
                            break;

                        case ContentTypes.Video:
                            screenContent.Add(new QuestionPreviewContentItem(
                                "video",
                                contentItem.IsRef
                                    ? ResolvePackageMedia(
                                        _qDocument.Video,
                                        QuestionPreviewMediaKind.Video,
                                        contentItem.Value)
                                    : contentItem.Value));
                            break;

                        case ContentTypes.Html:
                            _logger.LogInformation("HTML content was excluded from the controlled question preview");
                            screenContent.Add(new QuestionPreviewContentItem(
                                "text",
                                $"{Resources.Html}: {Resources.ExternalLinksAreForbidden}"));
                            break;

                        default:
                            break;
                    }
                    break;

                case ContentPlacements.Background:
                    var sound = contentItem.IsRef
                        ? ResolvePackageMedia(
                            _qDocument.Audio,
                            QuestionPreviewMediaKind.Audio,
                            contentItem.Value)
                        : contentItem.Value;

                    OnMessage(new QuestionPreviewContentMessage(
                        "background",
                        [new QuestionPreviewContentItem("audio", sound)]));
                    break;

                default:
                    break;
            }
        }

        if (_isAnswerSimple && content.FirstOrDefault() is { } rightAnswerContent)
        {
            OnMessage(new QuestionPreviewRightAnswerMessage(rightAnswerContent.Value));
        }

        if (screenContent.Count > 0)
        {
            OnMessage(new QuestionPreviewContentMessage("screen", screenContent));
        }
    }

    public void OnAskAnswer(string mode, int duration)
    {
        if (mode == StepParameterValues.AskAnswerMode_Button)
        {
            OnMessage(new QuestionPreviewSignalMessage(QuestionPreviewMessageTypes.BeginPressButton));
        }
        else
        {
            OnMessage(new QuestionPreviewReplicMessage(
                "s",
                _singleAnswerer ? Resources.YourAnswer : Resources.ThinkAll));
        }
    }

    public bool OnButtonPressStart() => false;

    public bool OnSetAnswerer(string mode, string? select, string? stakeVisibility)
    {
        var multipleAnswerers = mode == StepParameterValues.SetAnswererMode_All 
            || mode == StepParameterValues.SetAnswererMode_Stake && select == StepParameterValues.SetAnswererSelect_AllPossible;
        
        _singleAnswerer = !multipleAnswerers;
        return false;
    }

    public bool OnSetPrice(string mode, NumberSet? availableRange) => false;

    public bool OnSetTheme(string themeName) => false;

    public bool OnAccept() => false;

    public void OnQuestionStart(bool buttonsRequired, ICollection<string> rightAnswers, Action skipQuestionCallback)
    {
        _isAnswerSimple = false;
        _isAnswer = false;
        _rightAnswer = rightAnswers.FirstOrDefault() ?? "";

        OnMessage(new QuestionPreviewReadingSpeedMessage(0));
    }

    public void OnContentStart(IReadOnlyList<ContentItem> contentItems, Action<int> moveToContentCallback)
    {
        if (_isAnswer && !_isAnswerSimple)
        {
            OnMessage(new QuestionPreviewRightAnswerStartMessage(_rightAnswer));

            _isAnswer = false;
        }
    }

    public void OnSimpleRightAnswerStart() 
    { 
        _isAnswerSimple = true;
    }

    public void OnAnswerStart()
    {
        _isAnswer = true;
        OnMessage(new QuestionPreviewReplicMessage("s", ""));
    }

    public bool OnAnnouncePrice(NumberSet availableRange) => false;

    public bool OnAnswerOptions(AnswerOption[] answerOptions, IReadOnlyList<ContentItem[]> screenContentSequence)
    {
        var options = new List<AnswerOptionViewModel>();

        foreach (var option in answerOptions)
        {
            switch (option.Content.Type)
            {
                case ContentTypes.Text:
                    options.Add(new AnswerOptionViewModel(option.Label, new ContentInfo(ContentType.Text, option.Content.Value)));
                    break;

                case ContentTypes.Image:
                    options.Add(new AnswerOptionViewModel(
                        option.Label,
                        new ContentInfo(
                            ContentType.Image,
                            option.Content.IsRef
                                ? ResolvePackageMedia(
                                    _qDocument.Images,
                                    QuestionPreviewMediaKind.Image,
                                    option.Content.Value)
                                : option.Content.Value)));
                    break;

                default:
                    break;
            }
        }

        OnMessage(new QuestionPreviewAnswerOptionsLayoutMessage(
            true,
            options.Select(option => option.Content.Type.ToString().ToLowerInvariant()).ToArray()));

        for (int i = 0; i < options.Count; i++)
        {
            OnMessage(new QuestionPreviewAnswerOptionMessage(
                i,
                options[i].Label,
                options[i].Content.Type.ToString().ToLowerInvariant(),
                options[i].Content.Value));
        }

        _options = options.ToArray();

        return false;
    }

    public bool OnRightAnswerOption(string rightOptionLabel)
    {
        if (_options == null)
        {
            return false;
        }

        for (var i = 0; i < _options.Length; i++)
        {
            if (_options[i].Label == rightOptionLabel)
            {
                OnMessage(new QuestionPreviewContentStateMessage(
                    "screen",
                    i + 1,
                    2)); // right

                break;
            }
        }

        return true;
    }

    public bool OnRightAnswerPoint(string rightAnswer) => false;

    private void OnMessage(QuestionPreviewMessage message) =>
        SendJsonMessage?.Invoke(QuestionPreviewProtocol.Serialize(message));

    private string ResolvePackageMedia(
        MediaStorageViewModel storage,
        QuestionPreviewMediaKind kind,
        string name)
    {
        var media = storage.CreateQuestionPreviewSource(kind, name);
        if (_previewSession.TryGetMediaSource(media, out var source)
            && !string.IsNullOrWhiteSpace(source))
        {
            return source;
        }

        throw new InvalidOperationException("Package media could not be exposed through the controlled preview host.");
    }

    public bool OnNumericAnswerType(int deviation) => false;

    public bool OnPointAnswerType(double deviation) => false;

    public bool OnClientAnswerType() => false;

    protected override Task Close_Executed(object? arg)
    {
        Dispose();
        return base.Close_Executed(arg);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Play.CanBeExecuted = false;
        Replay.CanBeExecuted = false;
        _previewSession.Dispose();
        base.Dispose(disposing);
    }
}
