using SIQuester.ViewModel.Contracts;
using System.Windows.Input;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Represents one validated document recovery snapshot and its user-initiated operations.
/// </summary>
public sealed class RecoveryEntryViewModel : ModelViewBase
{
    private readonly DocumentRecoveryEntry _entry;
    private readonly IDocumentRecoveryService _recoveryService;
    private readonly IExternalLauncher _externalLauncher;
    private readonly Func<DocumentRecoveryEntry, CancellationToken, Task> _restore;
    private readonly Action<RecoveryEntryViewModel> _completed;
    private readonly Func<Exception, Task> _reportError;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _isBusy;
    private bool _isDiscardPending;
    private bool _isPreviewVisible;
    private string? _packageName;
    private int _roundCount;
    private int _themeCount;
    private int _questionCount;
    private int _mediaCount;

    public RecoveryEntryViewModel(
        DocumentRecoveryEntry entry,
        IDocumentRecoveryService recoveryService,
        IExternalLauncher externalLauncher,
        Func<DocumentRecoveryEntry, CancellationToken, Task> restore,
        Action<RecoveryEntryViewModel> completed,
        Func<Exception, Task> reportError)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _recoveryService = recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));
        _externalLauncher = externalLauncher ?? throw new ArgumentNullException(nameof(externalLauncher));
        _restore = restore ?? throw new ArgumentNullException(nameof(restore));
        _completed = completed ?? throw new ArgumentNullException(nameof(completed));
        _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));

        Preview = new AsyncCommand(PreviewAsync);
        Restore = new AsyncCommand(RestoreAsync);
        Reveal = new AsyncCommand(RevealAsync);
        ConfirmDiscard = new AsyncCommand(DiscardAsync);
        RequestDiscard = new SimpleCommand(_ => IsDiscardPending = true);
        CancelDiscard = new SimpleCommand(_ => IsDiscardPending = false);
    }

    public string RecoveryId => _entry.RecoveryId;

    public string SnapshotPath => _entry.SnapshotPath;

    public string? OriginalPath => _entry.OriginalPath;

    public bool HasOriginalPath => !string.IsNullOrWhiteSpace(_entry.OriginalPath);

    public string DisplayName => _entry.DisplayName;

    public DateTimeOffset SavedAt => _entry.SavedAtUtc.ToLocalTime();

    public long SnapshotLength => _entry.SnapshotLength;

    public bool IsStale => _entry.IsStale;

    public bool CanRestore => !IsBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            UpdateCommandStates();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRestore));
        }
    }

    public bool IsDiscardPending
    {
        get => _isDiscardPending;
        private set
        {
            if (_isDiscardPending == value)
            {
                return;
            }

            _isDiscardPending = value;
            OnPropertyChanged();
        }
    }

    public bool IsPreviewVisible
    {
        get => _isPreviewVisible;
        private set
        {
            if (_isPreviewVisible == value)
            {
                return;
            }

            _isPreviewVisible = value;
            OnPropertyChanged();
        }
    }

    public string? PackageName
    {
        get => _packageName;
        private set
        {
            if (_packageName == value)
            {
                return;
            }

            _packageName = value;
            OnPropertyChanged();
        }
    }

    public int RoundCount
    {
        get => _roundCount;
        private set
        {
            if (_roundCount == value)
            {
                return;
            }

            _roundCount = value;
            OnPropertyChanged();
        }
    }

    public int ThemeCount
    {
        get => _themeCount;
        private set
        {
            if (_themeCount == value)
            {
                return;
            }

            _themeCount = value;
            OnPropertyChanged();
        }
    }

    public int QuestionCount
    {
        get => _questionCount;
        private set
        {
            if (_questionCount == value)
            {
                return;
            }

            _questionCount = value;
            OnPropertyChanged();
        }
    }

    public int MediaCount
    {
        get => _mediaCount;
        private set
        {
            if (_mediaCount == value)
            {
                return;
            }

            _mediaCount = value;
            OnPropertyChanged();
        }
    }

    public IAsyncCommand Preview { get; }

    public IAsyncCommand Restore { get; }

    public IAsyncCommand Reveal { get; }

    public ICommand RequestDiscard { get; }

    public IAsyncCommand ConfirmDiscard { get; }

    public ICommand CancelDiscard { get; }

    private async Task PreviewAsync(object? parameter)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            using var document = await _recoveryService.LoadAsync(_entry, _lifetimeCancellation.Token);
            PackageName = document.Package.Name;
            RoundCount = document.Package.Rounds.Count;
            ThemeCount = document.Package.Rounds.Sum(round => round.Themes.Count);
            QuestionCount = document.Package.Rounds.Sum(
                round => round.Themes.Sum(theme => theme.Questions.Count));
            MediaCount = document.Images.Count + document.Audio.Count + document.Video.Count + document.Html.Count;
            IsPreviewVisible = true;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await _reportError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreAsync(object? parameter)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _restore(_entry, _lifetimeCancellation.Token);
            _completed(this);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await _reportError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RevealAsync(object? parameter)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _externalLauncher.RevealFileAsync(SnapshotPath, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await _reportError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DiscardAsync(object? parameter)
    {
        if (IsBusy || !IsDiscardPending)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _recoveryService.DiscardAsync(RecoveryId, _lifetimeCancellation.Token);
            _completed(this);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await _reportError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateCommandStates()
    {
        Preview.CanBeExecuted = !IsBusy;
        Restore.CanBeExecuted = !IsBusy;
        Reveal.CanBeExecuted = !IsBusy;
        ConfirmDiscard.CanBeExecuted = !IsBusy;
        ((SimpleCommand)RequestDiscard).CanBeExecuted = !IsBusy;
        ((SimpleCommand)CancelDiscard).CanBeExecuted = !IsBusy;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
        }

        base.Dispose(disposing);
    }
}
