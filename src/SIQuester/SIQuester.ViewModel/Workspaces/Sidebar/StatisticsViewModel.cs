using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Helpers;
using SIQuester.ViewModel.Properties;
using SIQuester.ViewModel.Workspaces.Sidebar;
using System.Text;
using Utils.Commands;

namespace SIQuester.ViewModel;

public sealed class StatisticsViewModel : WorkspaceViewModel
{
    private readonly QDocument _document;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly object _refreshSync = new();
    private CancellationTokenSource? _refreshCancellation;
    private Task _refreshTask = Task.CompletedTask;
    private CancellationTokenSource? _removeUnusedCancellation;
    private Task _removeUnusedTask = Task.CompletedTask;
    private bool _isDisposed;

    public override string Header => Resources.Statistic;

    private bool _checkEmptyAuthors = false;

    public bool CheckEmptyAuthors
    {
        get => _checkEmptyAuthors;
        set
        {
            if (_checkEmptyAuthors != value)
            {
                _checkEmptyAuthors = value;
                OnPropertyChanged();
                QueueRefresh();
            }
        }
    }

    private bool _checkEmptySources = false;

    public bool CheckEmptySources
    {
        get => _checkEmptySources;
        set
        {
            if (_checkEmptySources != value)
            {
                _checkEmptySources = value;
                OnPropertyChanged();
                QueueRefresh();
            }
        }
    }

    private bool _checkBrackets = true;

    public bool CheckBrackets
    {
        get => _checkBrackets;
        set
        {
            if (_checkBrackets != value)
            {
                _checkBrackets = value;
                OnPropertyChanged();
                QueueRefresh();
            }
        }
    }

    private string _result = "";

    /// <summary>
    /// Statistics result.
    /// </summary>
    public string Result
    {
        get => _result;
        set { _result = value; OnPropertyChanged(); }
    }

    private WarningViewModel[] _warnings = Array.Empty<WarningViewModel>();

    /// <summary>
    /// Statistics warnings.
    /// </summary>
    public WarningViewModel[] Warnings
    {
        get => _warnings;
        set
        {
            if (_warnings != value)
            {
                _warnings = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WarningCount));
                OnPropertyChanged(nameof(HasWarnings));
            }
        }
    }

    public int RoundCount { get; private set; }

    public int ThemeCount { get; private set; }

    public int QuestionCount { get; private set; }

    public int MediaFileCount { get; private set; }

    public int WarningCount => Warnings.Length;

    public bool HasWarnings => WarningCount > 0;

    private bool _isRefreshing;

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (_isRefreshing != value)
            {
                _isRefreshing = value;
                OnPropertyChanged();
            }
        }
    }

    public AsyncCommand Create { get; }

    public AsyncCommand RemoveUnusedFiles { get; }

    public StatisticsViewModel(QDocument document, IUiDispatcher uiDispatcher)
    {
        _document = document;
        _uiDispatcher = uiDispatcher;

        Create = new AsyncCommand(_ => QueueRefreshAsync());
        RemoveUnusedFiles = new AsyncCommand(RemoveUnusedFiles_ExecutedAsync);
        QueueRefresh();
    }

    private void QueueRefresh() => _ = QueueRefreshAsync();

    private Task QueueRefreshAsync()
    {
        CancellationTokenSource cancellation;
        Task refreshTask;

        lock (_refreshSync)
        {
            if (_isDisposed)
            {
                return Task.CompletedTask;
            }

            if (_removeUnusedCancellation != null)
            {
                return Task.CompletedTask;
            }

            _refreshCancellation?.Cancel();
            cancellation = new CancellationTokenSource();
            _refreshCancellation = cancellation;
            refreshTask = RefreshAsync(cancellation);
            _refreshTask = refreshTask;
        }

        return refreshTask;
    }

    private async Task RefreshAsync(CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        SetRefreshState(cancellation, true);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var warnings = new List<WarningViewModel>();
            var stats = new StringBuilder();
            var roundCount = _document.Document.Package.Rounds.Count;
            var themeCount = 0;
            var questionCount = 0;

            _document.Document.Package.Rounds.ForEach(round =>
            {
                themeCount += round.Themes.Count;
                round.Themes.ForEach(theme => questionCount += theme.Questions.Count);
            });

            stats.AppendLine($"{Resources.NumOfRounds}: {roundCount}");
            stats.AppendLine($"{Resources.NumOfThemes}: {themeCount}");
            stats.AppendLine($"{Resources.NumOfQuests}: {questionCount}");
            stats.AppendLine();
            CheckText(stats, warnings);

            var checkResult = await _document.CheckLinksAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            warnings.AddRange(checkResult.Item1);
            stats.Append(checkResult.Item2);

            await _uiDispatcher.InvokeAsync(
                () =>
                {
                    if (!IsCurrentRefresh(cancellation))
                    {
                        return;
                    }

                    RoundCount = roundCount;
                    ThemeCount = themeCount;
                    QuestionCount = questionCount;
                    MediaFileCount = _document.Images.Files.Count
                        + _document.Audio.Files.Count
                        + _document.Video.Files.Count
                        + _document.Html.Files.Count;
                    OnPropertyChanged(nameof(RoundCount));
                    OnPropertyChanged(nameof(ThemeCount));
                    OnPropertyChanged(nameof(QuestionCount));
                    OnPropertyChanged(nameof(MediaFileCount));
                    Result = stats.ToString();
                    Warnings = warnings.ToArray();
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exc)
        {
            OnError(exc);
        }
        finally
        {
            SetRefreshState(cancellation, false);
            cancellation.Dispose();
        }
    }

    private void CheckText(StringBuilder stats, List<WarningViewModel> warnings)
    {
        foreach (var round in _document.Package.Rounds)
        {
            stats.AppendLine(string.Format("{0}: {1}", Resources.Round, round.Model.Name));
            stats.AppendLine(string.Format("{0}: {1}", Resources.NumOfThemes, round.Themes.Count));

            foreach (var theme in round.Themes)
            {
                var themeData = new StringBuilder();
                bool here = false;

                var unrecognizedText = theme.Info.Comments.Text.Contains(Resources.Undefined);
                var noAuthors = theme.Info.Authors.Count == 0 && _checkEmptyAuthors;
                var invalidBrackets = _checkBrackets
                    && (!Utils.ValidateTextBrackets(theme.Model.Name) || !Utils.ValidateTextBrackets(theme.Info.Comments.Text));

                if (unrecognizedText || noAuthors || invalidBrackets)
                {
                    if (!here)
                    {
                        here = true;
                        themeData.AppendLine(string.Format("{0}: {1}", Resources.Theme, theme.Model.Name));
                    }

                    if (unrecognizedText)
                    {
                        themeData.AppendLine(Resources.Unrecognized);
                        warnings.Add(CreateWarning(Resources.Unrecognized, theme));
                    }

                    if (noAuthors)
                    {
                        themeData.AppendLine(Resources.NoAuthors);
                        warnings.Add(CreateWarning(Resources.NoAuthors, theme));
                    }

                    if (invalidBrackets)
                    {
                        themeData.AppendLine(Resources.WrongBrackets);
                        warnings.Add(CreateWarning(Resources.WrongBrackets, theme));
                    }
                }

                foreach (var question in theme.Questions)
                {
                    var questionText = question.Model.GetText();

                    var emptyNormal = question.Model.Price == Question.InvalidPrice || question.TypeName == QuestionTypes.SecretNoQuestion;
                    
                    var emptyQuestion = !emptyNormal
                        && (questionText == "" || questionText == Resources.Question)
                        && !question.Model.HasQuestionMedia();

                    var noAnswer = !emptyNormal && !question.Model.HasAnswer();

                    var emptySources = question.Info.Sources.Count == 0 && _checkEmptySources;

                    var bracketsData = new StringBuilder();

                    if (_checkBrackets)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            var text = string.Empty;
                            var comment = string.Empty;

                            switch (i)
                            {
                                case 0:
                                    text = question.Model.GetText();
                                    comment = Resources.QText;
                                    break;

                                case 1:
                                    if (question.Right.Count == 0)
                                        continue;

                                    text = question.Right[0];
                                    comment = Resources.QAnswer;
                                    break;

                                case 2:
                                    if (question.Info.Sources.Count == 0)
                                        continue;

                                    text = question.Info.Sources[0];
                                    comment = Resources.Source;
                                    break;

                                case 3:
                                    text = question.Info.Comments.Text;
                                    comment = Resources.Comment;
                                    break;
                            }

                            if (!Utils.ValidateTextBrackets(text))
                            {
                                bracketsData.Append(question.Model.Price);
                                bracketsData.Append(": ");
                                bracketsData.Append(Resources.WrongBrackets);
                                bracketsData.AppendFormat(" ({0})", comment);
                                bracketsData.Append(": ");
                                bracketsData.AppendLine(text);
                            }
                        }
                    }

                    var hasBracketsIssues = bracketsData.Length > 0;

                    if (emptyQuestion || noAnswer || emptySources || hasBracketsIssues)
                    {
                        if (!here)
                        {
                            here = true;
                            stats.AppendLine(string.Format("{0}: {1}", Resources.Theme, theme.Model.Name));
                        }
                    }

                    if (emptyQuestion)
                    {
                        themeData.AppendLine(string.Format("{0}: {1}", question.Model.Price, Resources.NoQuestion));
                        warnings.Add(CreateQuestionWarning(question, Resources.NoQuestion));
                    }

                    if (noAnswer)
                    {
                        themeData.AppendLine(string.Format("{0}: {1}", question.Model.Price, Resources.NoAnswer));
                        warnings.Add(CreateQuestionWarning(question, Resources.NoAnswer));
                    }

                    if (emptySources)
                    {
                        themeData.AppendLine(string.Format("{0}: {1}", question.Model.Price, Resources.NoSource));
                        warnings.Add(CreateQuestionWarning(question, Resources.NoSource));
                    }

                    if (hasBracketsIssues)
                    {
                        themeData.Append(bracketsData);
                        warnings.Add(CreateQuestionWarning(question, Resources.WrongBrackets));
                    }

                    foreach (var content in question.GetContent())
                    {
                        if (content.Type == ContentTypes.Audio && content.Model.Placement != ContentPlacements.Background)
                        {
                            var title = string.Format(Resources.AudioIsNotOnBackground, content.Model.Value);
                            themeData.AppendLine($"{question.Model.Price}: {title}");
                            warnings.Add(CreateQuestionWarning(question, title));
                        }
                    }
                }

                if (themeData.Length > 0)
                {
                    stats.AppendLine(themeData.ToString());
                }
            }

            stats.AppendLine();
        }
    }

    private WarningViewModel CreateWarning(string title, IItemViewModel source) =>
        new(title, () => _document.Navigate.Execute(source));

    private WarningViewModel CreateQuestionWarning(QuestionViewModel question, string title) =>
        CreateWarning($"{question.Model.Price}: {title}", question);

    private async Task RemoveUnusedFiles_ExecutedAsync(object? arg)
    {
        CancellationTokenSource cancellation;
        Task refreshTask;

        lock (_refreshSync)
        {
            if (_isDisposed || _removeUnusedCancellation != null)
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            _removeUnusedCancellation = cancellation;
            _refreshCancellation?.Cancel();
            refreshTask = _refreshTask;
            Create.CanBeExecuted = false;
            RemoveUnusedFiles.CanBeExecuted = false;
            _removeUnusedTask = RemoveUnusedFilesCoreAsync(cancellation, refreshTask);
        }

        await _removeUnusedTask;
    }

    private async Task RemoveUnusedFilesCoreAsync(CancellationTokenSource cancellation, Task refreshTask)
    {
        var refreshAfterRemoval = false;

        try
        {
            await refreshTask;
            cancellation.Token.ThrowIfCancellationRequested();
            await _document.RemoveUnusedFilesAsync(cancellation.Token);
            refreshAfterRemoval = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exc)
        {
            OnError(exc);
        }
        finally
        {
            lock (_refreshSync)
            {
                if (ReferenceEquals(_removeUnusedCancellation, cancellation))
                {
                    _removeUnusedCancellation = null;
                    Create.CanBeExecuted = !_isDisposed;
                    RemoveUnusedFiles.CanBeExecuted = !_isDisposed;
                }
            }

            cancellation.Dispose();
        }

        if (refreshAfterRemoval)
        {
            await QueueRefreshAsync();
        }
    }

    private bool IsCurrentRefresh(CancellationTokenSource cancellation)
    {
        lock (_refreshSync)
        {
            return !_isDisposed && ReferenceEquals(_refreshCancellation, cancellation);
        }
    }

    private void SetRefreshState(CancellationTokenSource cancellation, bool isRefreshing)
    {
        lock (_refreshSync)
        {
            if (_isDisposed || !ReferenceEquals(_refreshCancellation, cancellation))
            {
                return;
            }

            IsRefreshing = isRefreshing;
            Create.CanBeExecuted = !isRefreshing;

            if (!isRefreshing)
            {
                _refreshCancellation = null;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        Task refreshTask;
        Task removeUnusedTask;

        lock (_refreshSync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _refreshCancellation?.Cancel();
            _removeUnusedCancellation?.Cancel();
            refreshTask = _refreshTask;
            removeUnusedTask = _removeUnusedTask;
        }

        _ = ObserveRefreshCompletionAsync(refreshTask);
        _ = ObserveRefreshCompletionAsync(removeUnusedTask);
        base.Dispose(disposing);
    }

    private static async Task ObserveRefreshCompletionAsync(Task refreshTask)
    {
        try
        {
            await refreshTask;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
