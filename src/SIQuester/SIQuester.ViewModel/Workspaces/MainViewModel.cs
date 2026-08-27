using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIPackages;
using SIPackages.Exceptions;
using SIQuester.Model;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Helpers;
using SIQuester.ViewModel.Model;
using SIQuester.ViewModel.PlatformSpecific;
using SIQuester.ViewModel.Properties;
using SIQuester.ViewModel.Serializers;
using SIQuester.ViewModel.Services;
using SIStorageService.ViewModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using Utils;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Represents a main application model.
/// </summary>
public sealed class MainViewModel : ModelViewBase, INotifyPropertyChanged
{
    private const string DonateUrl = "https://yoomoney.ru/to/410012283941753";
    private const int MaxMessageLength = 1000;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MainViewModel> _logger;
    private readonly SemaphoreSlim _autoSaveGate = new(1, 1);

    public ILogger Logger => _logger;

    #region Commands

    /// <summary>
    /// Creates a new workspace.
    /// </summary>
    public ICommand New { get; private set; }

    /// <summary>
    /// Opens a file.
    /// </summary>
    public ICommand Open { get; private set; }

    /// <summary>
    /// Opens one of the recently opened files.
    /// </summary>
    public ICommand OpenRecent { get; private set; }

    /// <summary>
    /// Removes one of the recentlty opened files.
    /// </summary>
    public ICommand RemoveRecent { get; private set; }

    /// <summary>
    /// Imports text file.
    /// </summary>
    public ICommand ImportTxt { get; private set; }

    /// <summary>
    /// Imports XML file.
    /// </summary>
    public ICommand ImportXml { get; private set; }

    /// <summary>
    /// Imports YAML file.
    /// </summary>
    public ICommand ImportYaml { get; private set; }

    /// <summary>
    /// Imports quizz DB file.
    /// </summary>
    public ICommand ImportBase { get; private set; }

    /// <summary>
    /// Imports SIStorage file.
    /// </summary>
    public ICommand ImportFromSIStore { get; private set; }

    /// <summary>
    /// Saves all changed workspaces.
    /// </summary>
    public IAsyncCommand SaveAll { get; private set; }

    public ICommand About { get; private set; }

    public ICommand Feedback { get; private set; }

    public ICommand Donate { get; private set; }

    public ICommand SetSettings { get; private set; }

    public ICommand SearchFolder { get; private set; }

    /// <summary>
    /// Opens a help file.
    /// </summary>
    public ICommand Help { get; private set; }

    /// <summary>
    /// Closes main view.
    /// </summary>
    public IAsyncCommand Close { get; private set; }

    #endregion

    /// <summary>
    /// Opened workspaces.
    /// </summary>
    public ObservableCollection<WorkspaceViewModel> DocList { get; } = new();

    public bool HasWorkspaces => DocList.Count > 0;

    /// <summary>
    /// Validated recovery snapshots available for explicit user action in capable hosts.
    /// </summary>
    public ObservableCollection<RecoveryEntryViewModel> RecoveryEntries { get; } = new();

    public bool HasRecoveryEntries => RecoveryEntries.Count > 0;

    private QDocument? _activeDocument = null;
    private WorkspaceViewModel? _activeWorkspace;

    /// <summary>
    /// Gets or sets the selected workspace independently of a UI collection-view implementation.
    /// </summary>
    public WorkspaceViewModel? ActiveWorkspace
    {
        get => _activeWorkspace;
        set
        {
            if (_activeWorkspace == value)
            {
                return;
            }

            _activeWorkspace = value;
            OnPropertyChanged();
            ActiveDocument = value as QDocument;
        }
    }

    /// <summary>
    /// Currently opened document.
    /// </summary>
    public QDocument? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (_activeDocument != value)
            {
                _activeDocument = value;
                OnPropertyChanged();
            }
        }
    }

    public AppSettings Settings => AppSettings.Default;

    private readonly string[] _args;
    private readonly AppOptions _appOptions;
    private readonly IClipboardService _clipboardService;
    private readonly IPlatformService _platformService;
    private readonly IDocumentViewModelFactory _documentViewModelFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly IApplicationLifetimeService _applicationLifetimeService;
    private readonly IDocumentRecoveryService _documentRecoveryService;
    private readonly IPlatformCapabilities _platformCapabilities;
    private readonly IExternalLauncher _externalLauncher;

    public AppOptions AppOptions => _appOptions;

    public MainViewModel(
        string[] args,
        AppOptions appOptions,
        IClipboardService clipboardService,
        IServiceProvider serviceProvider,
        IPlatformService platformService,
        IDocumentViewModelFactory documentViewModelFactory,
        ILoggerFactory loggerFactory,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        IApplicationLifetimeService applicationLifetimeService,
        IDocumentRecoveryService documentRecoveryService,
        IPlatformCapabilities platformCapabilities,
        IExternalLauncher externalLauncher)
    {
        _loggerFactory = loggerFactory;
        _clipboardService = clipboardService;
        _platformService = platformService;
        _documentViewModelFactory = documentViewModelFactory;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _applicationLifetimeService = applicationLifetimeService;
        _documentRecoveryService = documentRecoveryService;
        _platformCapabilities = platformCapabilities;
        _externalLauncher = externalLauncher;
        _logger = loggerFactory.CreateLogger<MainViewModel>();
        _appOptions = appOptions;

        DocList.CollectionChanged += DocList_CollectionChanged;
        RecoveryEntries.CollectionChanged += RecoveryEntries_CollectionChanged;

        Open = new AsyncCommand(Open_ExecutedAsync);
        OpenRecent = new AsyncCommand(OpenRecent_ExecutedAsync);
        RemoveRecent = new SimpleCommand(RemoveRecent_Executed);

        ImportTxt = new SimpleCommand(ImportTxt_Executed);
        ImportXml = new SimpleCommand(ImportXml_Executed);
        ImportYaml = new SimpleCommand(ImportYaml_Executed);
        ImportBase = new SimpleCommand(ImportBase_Executed);
        ImportFromSIStore = new SimpleCommand(ImportFromSIStore_Executed);

        SaveAll = new AsyncCommand(SaveAll_ExecutedAsync) { CanBeExecuted = false };

        About = new SimpleCommand(About_Executed);
        Feedback = new SimpleCommand(Feedback_Executed);
        Donate = new SimpleCommand(Donate_Executed);

        SetSettings = new SimpleCommand(SetSettings_Executed);
        SearchFolder = new SimpleCommand(SearchFolder_Executed);

        _serviceProvider = serviceProvider;

        New = new SimpleCommand(New_Executed);
        Help = new SimpleCommand(Help_Executed);
        Close = new AsyncCommand(Close_Executed);

        _args = args;

        UI.Initialize();
    }

    public async Task InitializeAsync()
    {
        await RestoreRecoveryEntriesAsync();

        if (_args.Length > 0 && !IsDocumentPathOpen(_args[0]))
        {
            await OpenFileAsync(_args[0]);
        }

        if (!AppSettings.Default.AutoSave)
        {
            return;
        }

        var autoSaveFolder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), AppSettings.ProductName, AppSettings.AutoSaveSimpleFolderName));

        if (!autoSaveFolder.Exists)
        {
            return;
        }

        var folders = autoSaveFolder.EnumerateDirectories();

        if (!folders.Any())
        {
            return;
        }

        try
        {
            _logger.LogInformation("Unsaved files found");

            if (await _dialogService.ConfirmAsync(Resources.RestoreConfirmation))
            {
                foreach (var folder in folders)
                {
                    var originFileName = PathHelper.DecodePath(folder.Name);
                    _logger.LogInformation("Restoring file {file}...", originFileName);

                    var document = await OpenFileAsync(originFileName);
                    document?.RestoreFromFolder(folder);
                }
            }
            else
            {
                foreach (var folder in folders)
                {
                    folder.Delete(true);
                }
            }
        }
        catch (Exception exc)
        {
            ReportError(exc);
        }
    }

    private async Task RestoreRecoveryEntriesAsync()
    {
        var entries = await _documentRecoveryService.ListAsync();
        var recoverableEntries = entries.Where(entry => !entry.IsStale).ToArray();

        foreach (var staleEntry in entries.Where(entry => entry.IsStale))
        {
            _logger.LogInformation(
                "Stale recovery entry {RecoveryId} was retained because its canonical file is newer",
                staleEntry.RecoveryId);
        }

        if (_platformCapabilities.SupportsRecoveryManagementUi)
        {
            foreach (var entry in entries)
            {
                RecoveryEntries.Add(new RecoveryEntryViewModel(
                    entry,
                    _documentRecoveryService,
                    _externalLauncher,
                    RestoreRecoveryEntryAsync,
                    CompleteRecoveryEntry,
                    exception => ReportErrorAsync(exception, null)));
            }

            if (entries.Count > 0)
            {
                _logger.LogInformation(
                    "{RecoveryCount} document snapshots are available in the recovery center",
                    entries.Count);
            }

            return;
        }

        if (recoverableEntries.Length == 0)
        {
            return;
        }

        _logger.LogInformation("{RecoveryCount} recoverable document snapshots were found", recoverableEntries.Length);

        if (!await _dialogService.ConfirmAsync(Resources.RestoreConfirmation))
        {
            foreach (var entry in recoverableEntries)
            {
                await _documentRecoveryService.DiscardAsync(entry.RecoveryId);
            }

            return;
        }

        foreach (var entry in recoverableEntries)
        {
            try
            {
                await RestoreRecoveryEntryAsync(entry, CancellationToken.None);
            }
            catch (Exception exception)
            {
                await ReportErrorAsync(exception, null);
            }
        }
    }

    private async Task RestoreRecoveryEntryAsync(
        DocumentRecoveryEntry entry,
        CancellationToken cancellationToken)
    {
        SIDocument? document = null;

        try
        {
            document = await _documentRecoveryService.LoadAsync(entry, cancellationToken);
            var viewModel = _documentViewModelFactory.CreateViewModelFor(document, entry.DisplayName);
            document = null;
            // A stale snapshot must never target a newer canonical package. It is restored as
            // an unsaved copy so the next save requires an explicit destination.
            viewModel.Path = entry.IsStale ? string.Empty : entry.OriginalPath ?? string.Empty;
            viewModel.AdoptRecoveryId(entry.RecoveryId);
            viewModel.Changed = true;
            DocList.Add(viewModel);
            _logger.LogInformation("Recovery entry {RecoveryId} was opened", entry.RecoveryId);
        }
        finally
        {
            document?.Dispose();
        }
    }

    private void CompleteRecoveryEntry(RecoveryEntryViewModel recoveryEntry)
    {
        if (RecoveryEntries.Remove(recoveryEntry))
        {
            recoveryEntry.Dispose();
        }
    }

    private void RecoveryEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(HasRecoveryEntries));

    private bool IsDocumentPathOpen(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return DocList
            .OfType<QDocument>()
            .Any(document => !string.IsNullOrWhiteSpace(document.Path)
                && string.Equals(Path.GetFullPath(document.Path), fullPath, comparison));
    }

    private void SearchFolder_Executed(object? arg) => DocList.Add(new SearchFolderViewModel(this));

    private void SetSettings_Executed(object? arg) => DocList.Add(new SettingsViewModel(_platformService));

    private void Help_Executed(object? arg) => _platformService.ShowHelp();

    private async Task Close_Executed(object? arg)
    {
        _logger.LogInformation("Close_Executed");

        if (await TryCloseAsync())
        {
            _logger.LogInformation("Close_Executed complete");
            _applicationLifetimeService.RequestExit();
        }
    }

    public async Task<bool> TryCloseAsync()
    {
        _logger.LogInformation("TryCloseAsync started");

        foreach (var doc in DocList.ToArray())
        {
            await doc.Close.ExecuteAsync(null);

            if (DocList.Contains(doc)) // Closing has been cancelled
            {
                _logger.LogInformation("TryCloseAsync cancelled");
                return false;
            }
        }

        _logger.LogInformation("TryCloseAsync completed");
        return true;
    }

    private void Feedback_Executed(object? arg) => OpenUri(Resources.AuthorSiteUrl);

    private void Donate_Executed(object? arg) => OpenUri(DonateUrl);

    private void OpenUri(string uri)
    {
        try
        {
            Browser.Open(uri);
        }
        catch (Exception exc)
        {
            ReportError(exc);
        }
    }

    private void About_Executed(object? arg) => DocList.Add(new AboutViewModel());

    private void DocList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (WorkspaceViewModel item in e.NewItems ?? Array.Empty<WorkspaceViewModel>())
                {
                    item.Error += ReportError;
                    item.NewItem += Item_NewDoc;
                    item.Closed += Item_Closed;
                    ActiveWorkspace = item;
                }

                CheckSaveAllCanBeExecuted(this, EventArgs.Empty);
                OnPropertyChanged(nameof(HasWorkspaces));
                break;

            case NotifyCollectionChangedAction.Move:
                break;

            case NotifyCollectionChangedAction.Remove:
                foreach (WorkspaceViewModel item in e.OldItems ?? Array.Empty<WorkspaceViewModel>())
                {
                    item.Error -= ReportError;
                    item.NewItem -= Item_NewDoc;
                    item.Closed -= Item_Closed;

                    if (ActiveWorkspace == item)
                    {
                        ActiveWorkspace = DocList.LastOrDefault();
                    }
                }

                CheckSaveAllCanBeExecuted(this, EventArgs.Empty);
                OnPropertyChanged(nameof(HasWorkspaces));
                break;

            default:
                break;
        }
    }

    private void Item_NewDoc(WorkspaceViewModel doc)
    {
        DocList.Add(doc);
    }

    private void Item_Closed(WorkspaceViewModel doc)
    {
        doc.Dispose();
        DocList.Remove(doc);
    }

    private void CheckSaveAllCanBeExecuted(object sender, EventArgs e)
    {
        SaveAll.CanBeExecuted = DocList.Count > 0;
    }

    /// <summary>
    /// Allows to create new document.
    /// </summary>
    private void New_Executed(object? arg) =>
        DocList.Add(new NewViewModel(
            Settings,
            _serviceProvider.GetRequiredService<IPackageTemplatesRepository>(),
            _documentViewModelFactory,
            _loggerFactory));

    /// <summary>
    /// Открыть существующий пакет
    /// </summary>
    private async Task Open_ExecutedAsync(object? arg)
    {
        if (arg is string filename)
        {
            await OpenFileAsync(filename);
            return;
        }

        var files = await _filePickerService.PickOpenFilesAsync(new OpenFilePickerRequest(
            null,
            new[] { new FileTypeFilter(Resources.SIQuestions, new[] { AppSettings.SiqExtension }) }));

        foreach (var file in files)
        {
            if (file.LocalPath == null)
            {
                await _dialogService.ShowErrorAsync(Resources.FileOpenError);
                continue;
            }

            await OpenFileAsync(file.LocalPath);
        }
    }

    /// <summary>
    /// Opens recently opened file.
    /// </summary>
    /// <param name="arg">Path to the file.</param>
    private async Task OpenRecent_ExecutedAsync(object? arg)
    {
        var filePath = arg?.ToString();

        if (filePath == null)
        {
            return;
        }

        await OpenFileAsync(filePath);
    }

    /// <summary>
    /// Removes recent file.
    /// </summary>
    /// <param name="arg">Recent file path.</param>
    private void RemoveRecent_Executed(object? arg)
    {
        var filePath = arg?.ToString();

        if (filePath == null)
        {
            return;
        }

        AppSettings.Default.History.Remove(filePath);
    }

    /// <summary>
    /// Opens the existing file.
    /// </summary>
    /// <param name="path">File path.</param>
    internal async Task<QDocument?> OpenFileAsync(string path)
    {
        Task<QDocument> loader(CancellationToken cancellationToken) => Task.Run(() =>
        {
            FileStream? stream = null;
            SIDocument? document = null;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream = File.OpenRead(path);

                // Loads in read only mode to keep file LastUpdate time unmodified
                document = SIDocument.Load(stream);
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation("Document has been successfully opened. Path: {path}", path);

                var docViewModel = _documentViewModelFactory.CreateViewModelFor(
                    document,
                    Path.GetFileNameWithoutExtension(path));
                docViewModel.Path = path;

                docViewModel.CheckFileSize();

                document = null; // Ownership has moved to the returned view model.
                return docViewModel;
            }
            catch (Exception exc)
            {
                document?.Dispose();
                stream?.Dispose();

                if (exc is UnauthorizedAccessException && (new FileInfo(path).Attributes & FileAttributes.ReadOnly) > 0)
                {
                    throw new Exception(Resources.FileIsReadOnly, exc);
                }

                throw;
            }
        }, cancellationToken);

        var loaderViewModel = new DocumentLoaderViewModel(path);
        DocList.Add(loaderViewModel);

        try
        {
            var document = await loaderViewModel.LoadAsync(loader);
            AppSettings.Default.History.Add(path);
            return document;
        }
        catch (InvalidDataException exc)
        {
            _logger.LogError(exc, "File {path} open error: {error}", path, exc.Message);
            await ShowCorruptedPackageErrorAsync(path);
            return null;
        }
        catch (Exception exc)
        {
            if (exc is FileNotFoundException)
            {
                AppSettings.Default.History.Remove(path);
            }

            _logger.LogError(exc, "File {path} open error: {error}", path, exc.Message);
            ReportError(exc, Resources.FileOpenError);
            return null;
        }
    }

    private async Task ShowCorruptedPackageErrorAsync(string path)
    {
        const string openAutosave = "open-autosave";
        const string openLogs = "open-logs";

        var autoSavePath = Path.Combine(
            Path.GetTempPath(),
            AppSettings.ProductName,
            AppSettings.AutoSaveSimpleFolderName,
            PathHelper.EncodePath(path));

        var logsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
        var title = new StringBuilder(Resources.PackageCorruptedHint);
        var options = new List<DialogOption>();

        if (Directory.Exists(autoSavePath))
        {
            title.Append(Resources.PackageCorruptedHintAutoSave);
            options.Add(new DialogOption(openAutosave, Resources.OpenAutosaveFolder));
        }

        if (Directory.Exists(logsPath))
        {
            title.Append(". ").Append(Resources.PackageCorruptedHintLogs);
            options.Add(new DialogOption(openLogs, Resources.OpenLogsFolder));
        }

        options.Add(new DialogOption("close", Resources.Close));
        var selectedOption = await _dialogService.SelectOptionAsync(title.ToString(), options);

        if (selectedOption == openAutosave)
        {
            OpenFolder(autoSavePath);
        }
        else if (selectedOption == openLogs)
        {
            OpenFolder(logsPath);
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Browser.Open(path);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    /// <summary>
    /// Imports text from file.
    /// </summary>
    private void ImportTxt_Executed(object? arg)
    {
        try
        {
            ITextSource? textSource = arg switch
            {
                string filePath => new FileTextSource(filePath),
                Stream stream => new StreamTextSource(stream),
                null => null,
                _ => throw new InvalidOperationException($"Incorrect text source: {arg}"),
            };

            var model = new ImportTextViewModel(_appOptions, _clipboardService, _documentViewModelFactory);
            DocList.Add(model);

            if (textSource != null)
            {
                model.Import(textSource);
            }
        }
        catch (Exception exc)
        {
            ReportError(exc);
        }
    }

    private void ImportXml_Executed(object? arg)
    {
        var file = PlatformManager.Instance.ShowImportUI("xml", Resources.XmlFilesFilter);

        if (file == null)
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(file);
            var doc = SIDocument.LoadXml(stream);

            var docViewModel = _documentViewModelFactory.CreateViewModelFor(doc, Path.GetFileNameWithoutExtension(file));

            docViewModel.Path = "";
            docViewModel.Changed = true;

            var mediaFolder = Path.GetDirectoryName(file);

            if (mediaFolder != null)
            {
                docViewModel.LoadMediaFromFolder(mediaFolder);
            }

            DocList.Add(docViewModel);
        }
        catch (Exception exc)
        {
            ReportError(exc);
        }
    }

    private void ImportYaml_Executed(object? arg)
    {
        var file = PlatformManager.Instance.ShowImportUI("yaml", Resources.YamlFilesFilter);

        if (file == null)
        {
            return;
        }

        try
        {
            Package package;

            using (var reader = new StreamReader(file))
            {
                package = YamlSerializer.DeserializePackage(reader);
            }

            var doc = SIDocument.Create(package);

            var docViewModel = _documentViewModelFactory.CreateViewModelFor(doc, Path.GetFileNameWithoutExtension(file));
            docViewModel.Path = "";
            docViewModel.Changed = true;

            var mediaFolder = Path.GetDirectoryName(file);

            if (mediaFolder != null)
            {
                docViewModel.LoadMediaFromFolder(mediaFolder);
            }

            DocList.Add(docViewModel);
        }
        catch (Exception exc)
        {
            ReportError(exc);
        }
    }

    /// <summary>
    /// Imports package from Packages Database.
    /// </summary>
    private void ImportBase_Executed(object? arg) =>
        DocList.Add(new ImportDBStorageViewModel(
            _documentViewModelFactory,
            _serviceProvider.GetRequiredService<IChgkDbClient>(),
            _appOptions));

    /// <summary>
    /// Imports package from SI Storage.
    /// </summary>
    private async void ImportFromSIStore_Executed(object? arg)
    {
        var importViewModel = new ImportSIStorageViewModel(
            _serviceProvider.GetRequiredService<StorageViewModel>(),
            _appOptions,
            _documentViewModelFactory);

        DocList.Add(importViewModel);

        await importViewModel.OpenAsync();
    }

    public async Task AutoSaveAsync(CancellationToken cancellationToken = default)
    {
        if (!await _autoSaveGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            foreach (var item in DocList.ToArray())
            {
                try
                {
                    await item.SaveToTempAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exc)
                {
                    await ReportErrorAsync(exc, null);
                }
            }
        }
        finally
        {
            _autoSaveGate.Release();
        }
    }

    private async Task SaveAll_ExecutedAsync(object? arg)
    {
        foreach (var item in DocList.ToArray())
        {
            try
            {
                await item.SaveIfNeededAsync();
            }
            catch (Exception exc)
            {
                ReportError(exc);
            }
        }
    }

    private void ReportError(Exception exception, string? message = null) =>
        _ = ReportErrorAsync(exception, message);

    private async Task ReportErrorAsync(Exception exception, string? message)
    {
        try
        {
            await _dialogService.ShowErrorAsync(BuildErrorMessage(exception, message));
        }
        catch (Exception dialogException)
        {
            _logger.LogError(dialogException, "Error dialog failed while reporting {Error}", exception.Message);
        }
    }

    private static string BuildErrorMessage(Exception exception, string? message)
    {
        if (exception is UnsupportedPackageVersionException unsupportedVersionException)
        {
            return string.Format(
                Resources.UnsupportedVersion,
                unsupportedVersionException.ActualVersion,
                unsupportedVersionException.MaximumSupportedVersion);
        }

        var fullMessage = message != null ? $"{message}: {exception.Message}" : exception.Message;
        return fullMessage.Length > MaxMessageLength
            ? string.Concat(fullMessage.AsSpan(0, MaxMessageLength), "…")
            : fullMessage;
    }

    public static void ShowError(Exception exc, string? message = null)
    {
        PlatformManager.Instance.ShowExclamationMessage(BuildErrorMessage(exc, message));
    }

    protected override void Dispose(bool disposing)
    {
        DocList.CollectionChanged -= DocList_CollectionChanged;
        RecoveryEntries.CollectionChanged -= RecoveryEntries_CollectionChanged;

        foreach (var item in DocList)
        {
            item.Dispose();
        }

        foreach (var recoveryEntry in RecoveryEntries)
        {
            recoveryEntry.Dispose();
        }

        base.Dispose(disposing);
    }
}
