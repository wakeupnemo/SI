using Microsoft.Extensions.Logging;
using SIPackages;
using SIPackages.Core;
using SIPackages.Models;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Helpers;
using SIQuester.ViewModel.Model;
using SIQuester.ViewModel.PlatformSpecific;
using SIQuester.ViewModel.Properties;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Defines sorting field options for media storage.
/// </summary>
public enum MediaSortField
{
    Name,
    Size
}

/// <summary>
/// Defines sorting direction options.
/// </summary>
public enum SortDirection
{
    Ascending,
    Descending
}

/// <summary>
/// Defines a media storage view model.
/// </summary>
public sealed class MediaStorageViewModel : WorkspaceViewModel
{
    private readonly QDocument _document;

    private readonly int _internalId = Random.Shared.Next();

    /// <summary>
    /// Добавленные файлы
    /// </summary>
    private readonly List<MediaItemViewModel> _added = new();

    /// <summary>
    /// Удалённые файлы
    /// </summary>
    private readonly List<MediaItemViewModel> _removed = new();

    /// <summary>
    /// Переименованные файлы
    /// </summary>
    private readonly List<Tuple<string, string>> _renamed = new();

    /// <summary>
    /// Пути для файлов, ещё не загруженных в коллекцию (не закоммиченных)
    /// </summary>
    private readonly Dictionary<MediaItemViewModel, PendingMediaFile> _streams = new();
    private readonly Dictionary<MediaItemViewModel, PendingMediaFile> _removedStreams = new();

    private bool _blockFlag = false;

    private bool _hasPendingChanges = false;

    private bool _sortingScheduled = false;

    public bool HasPendingChanges
    {
        get => _hasPendingChanges;
        set
        {
            if (_hasPendingChanges != value)
            {
                _hasPendingChanges = value;
                OnPropertyChanged();

                if (_hasPendingChanges)
                {
                    HasChanged?.Invoke();
                }
            }
        }
    }

    internal event Action<IChange>? Changed;

    internal void OnChanged(IChange change) => Changed?.Invoke(change);

    /// <summary>
    /// Collection files.
    /// </summary>
    public ObservableCollection<MediaItemViewModel> Files { get; } = new();

    private MediaItemViewModel? _currentFile = null;

    /// <summary>
    /// Current selected file.
    /// </summary>
    public MediaItemViewModel? CurrentFile
    {
        get => _currentFile;
        set
        {
            if (_currentFile != value)
            {
                _currentFile = value;
                OnPropertyChanged();
                UpdateCurrentFileCommands();
            }
        }
    }

    public ICommand AddItem { get; private set; }

    /// <summary>
    /// Adds files through the platform-neutral picker.
    /// </summary>
    public AsyncCommand AddFiles { get; }

    public ICommand DeleteItem { get; private set; }

    /// <summary>
    /// Removes the selected file when it has no package references.
    /// </summary>
    public SimpleCommand RemoveCurrentFile { get; }

    /// <summary>
    /// Links the selected file to the active question.
    /// </summary>
    public SimpleCommand LinkCurrentToQuestion { get; }

    /// <summary>
    /// Compresses media item.
    /// </summary>
    public ICommand CompressItem { get; private set; }

    /// <summary>
    /// Navigates to media item usage.
    /// </summary>
    public ICommand NavigateToUsage { get; }

    private readonly string _header;

    private readonly string _name;

    private readonly string _temporaryMediaDirectory;

    internal string Name => _name;

    public override string Header => _header;

    public event Action? HasChanged;

    private bool _isAddingFiles;
    private CancellationTokenSource? _addFilesCancellation;

    public bool IsAddingFiles
    {
        get => _isAddingFiles;
        private set
        {
            if (_isAddingFiles != value)
            {
                _isAddingFiles = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasCurrentFile => CurrentFile != null;

    public bool IsCurrentFileReferenced => CurrentFile != null
        && _document.HasMediaReference(_name, CurrentFile.Model.Name);

    public bool CanRemoveCurrentFile => CurrentFile != null && !IsCurrentFileReferenced;

    public bool CanLinkCurrentToQuestion => CurrentFile != null && _document.ActiveNode is QuestionViewModel;

    private readonly ILogger<MediaStorageViewModel> _logger;
    private readonly IMediaPreviewService _mediaPreviewService;

    private string _filter = "";

    /// <summary>
    /// Storage files names filter.
    /// </summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (_filter != value)
            {
                _filter = value;
                OnPropertyChanged();
            }
        }
    }

    private MediaSortField _sortField = MediaSortField.Name;
    private SortDirection _sortDirection = SortDirection.Ascending;

    /// <summary>
    /// Current sort field (Name or Size).
    /// </summary>
    public MediaSortField SortField
    {
        get => _sortField;
        set
        {
            if (_sortField != value)
            {
                _sortField = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Current sort direction (Ascending or Descending).
    /// </summary>
    public SortDirection SortDirection
    {
        get => _sortDirection;
        set
        {
            if (_sortDirection != value)
            {
                _sortDirection = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Available sort fields.
    /// </summary>
    public MediaSortField[] SortFields { get; } = new[] { MediaSortField.Name, MediaSortField.Size };

    /// <summary>
    /// Available sort directions.
    /// </summary>
    public SortDirection[] SortDirections { get; } = new[] { SortDirection.Ascending, SortDirection.Descending };

    public MediaStorageViewModel(
        QDocument document,
        DataCollection collection,
        string header,
        string temporaryMediaDirectory,
        ILogger<MediaStorageViewModel> logger,
        IMediaPreviewService mediaPreviewService,
        bool canCompress = false)
    {
        _document = document;
        _header = header;
        _name = collection.Name;
        _temporaryMediaDirectory = temporaryMediaDirectory;
        _logger = logger;
        _mediaPreviewService = mediaPreviewService;

        FillFiles(collection);

        AddItem = new SimpleCommand(AddItem_Executed);
        AddFiles = new AsyncCommand(AddFiles_ExecutedAsync);
        DeleteItem = new SimpleCommand(Delete_Executed);
        RemoveCurrentFile = new SimpleCommand(RemoveCurrentFile_Executed);
        LinkCurrentToQuestion = new SimpleCommand(LinkCurrentToQuestion_Executed);
        CompressItem = new SimpleCommand(CompressItem_Executed) { CanBeExecuted = canCompress };
        NavigateToUsage = new SimpleCommand(NavigateToUsage_Executed);
        UpdateCurrentFileCommands();
    }

    private void FillFiles(DataCollection collection)
    {
        foreach (var item in collection)
        {
            var named = CreateItem(item);
            Files.Add(named);
        }

        // Apply initial sorting after all items are loaded
        ApplySorting();
    }

    private MediaItemViewModel CreateItem(string item)
    {
        var model = new Named(item);
        var named = new MediaItemViewModel(
            model,
            _name,
            () => Wrap(model.Name),
            () => TryGetStreamInfo(model.Name),
            _mediaPreviewService);
        AttachItem(named);
        return named;
    }

    private void AttachItem(MediaItemViewModel item)
    {
        item.Model.PropertyChanged -= Named_PropertyChanged;
        item.PropertyChanged -= MediaItem_PropertyChanged;
        item.Model.PropertyChanged += Named_PropertyChanged;
        item.PropertyChanged += MediaItem_PropertyChanged;
    }

    private async void MediaItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // If MediaSource is loaded and we're sorting by size, re-apply sorting
        if (e.PropertyName == nameof(MediaItemViewModel.MediaSource) && 
            _sortField == MediaSortField.Size &&
            !_sortingScheduled)
        {
            _sortingScheduled = true;
            
            // Schedule re-sorting to avoid multiple rapid updates
            await Task.Delay(100);
            
            if (_sortField == MediaSortField.Size) // Check again in case sort field changed
            {
                ApplySorting();
            }
            
            _sortingScheduled = false;
        }
    }

    private void Named_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_blockFlag || sender == null)
        {
            return;
        }

        var ext = (ExtendedPropertyChangedEventArgs<string>)e;
        var item = (Named)sender;

        if (Files.Count(obj => obj.Model.Name == item.Name) > 1)
        {
            SafeRename(item, ext.OldValue);
            return;
        }

        var newValue = item.Name;
        var renamedExisting = !_added.Any(mi => mi.Model == item);
        Tuple<string, string>? tuple = null;

        if (renamedExisting)
        {
            tuple = Tuple.Create(ext.OldValue, item.Name.Replace("%", ""));
            _renamed.Add(tuple);
        }

        var contentType = CollectionNames.TryGetContentType(_name) ?? _name;
        _document.RenameContentReference(contentType, ext.OldValue, item.Name);

        OnChanged(new CustomChange(
            () =>
            {
                if (renamedExisting)
                {
                    var found = false;

                    for (int i = _renamed.Count - 1; i >= 0; i--)
                    {
                        if (_renamed[i] == tuple)
                        {
                            _renamed.RemoveAt(i);
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        _renamed.Add(Tuple.Create(item.Name, ext.OldValue));
                        HasPendingChanges = IsChanged();
                    }
                }

                SafeRename(item, ext.OldValue);
                _document.RenameContentReference(contentType, item.Name, ext.OldValue);
            },
            () =>
            {
                SafeRename(item, newValue);
                _document.RenameContentReference(contentType, ext.OldValue, item.Name);

                if (renamedExisting && tuple != null)
                {
                    var last = _renamed.LastOrDefault();

                    if (last != null && last.Item1 == tuple.Item2 && last.Item2 == tuple.Item1)
                    {
                        _renamed.RemoveAt(_renamed.Count - 1);
                    }
                    else
                    {
                        _renamed.Add(tuple);
                    }

                    HasPendingChanges = IsChanged();
                }
            }));

        HasPendingChanges = IsChanged();
    }

    private void SafeRename(Named item, string name)
    {
        _blockFlag = true;

        try
        {
            item.Name = name;
        }
        finally
        {
            _blockFlag = false;
        }
    }

    private bool IsChanged() => _added.Count > 0 || _removed.Count > 0 || _renamed.Count > 0;

    private void Delete_Executed(object? arg)
    {
        if (arg == null)
        {
            return;
        }

        try
        {
            var item = (MediaItemViewModel)arg;
            PreviewRemove(item);

            OnChanged(new CustomChange(
                () =>
                {
                    if (_removed.Contains(item))
                    {
                        _removed.Remove(item);
                    }
                    else if (_removedStreams.ContainsKey(item))
                    {
                        _added.Add(item);
                        _streams.Add(item, _removedStreams[item]);
                        _removedStreams.Remove(item);
                    }
                    else
                    {
                        return; // file was removed and removal has been committed
                    }

                    AttachItem(item);
                    Files.Add(item);
                    OnPropertyChanged(nameof(Files));

                    HasPendingChanges = IsChanged();
                },
                () =>
                {
                    PreviewRemove(item);
                    HasPendingChanges = IsChanged();
                }));

            HasPendingChanges = IsChanged();
        }
        catch (Exception exc)
        {
            OnError(exc);
        }
    }

    private async Task AddFiles_ExecutedAsync(object? arg)
    {
        if (_isAddingFiles)
        {
            return;
        }

        IsAddingFiles = true;
        UpdateCurrentFileCommands();
        var cancellation = new CancellationTokenSource();
        _addFilesCancellation = cancellation;

        try
        {
            var extensions = Quality.FileExtensions[_name]
                .Select(extension => extension.TrimStart('.'))
                .ToArray();
            var pickedFiles = await _document.FilePickerService.PickOpenFilesAsync(new OpenFilePickerRequest(
                Header,
                [new FileTypeFilter(Header, extensions)],
                AllowMultiple: true), cancellation.Token);

            if (pickedFiles.Count == 0)
            {
                return;
            }

            var stagedFiles = new List<StagedMediaFile>();

            try
            {
                foreach (var pickedFile in pickedFiles)
                {
                    stagedFiles.Add(await StageFileAsync(pickedFile, cancellation.Token));
                }

                cancellation.Token.ThrowIfCancellationRequested();
                using var change = _document.OperationsManager.BeginComplexChange();
                MediaItemViewModel? lastAdded = null;

                foreach (var stagedFile in stagedFiles)
                {
                    lastAdded = AddFile(stagedFile);
                }

                change.Commit();
                CurrentFile = lastAdded;
            }
            finally
            {
                foreach (var stagedFile in stagedFiles)
                {
                    stagedFile.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exc)
        {
            OnError(exc);
        }
        finally
        {
            if (ReferenceEquals(_addFilesCancellation, cancellation))
            {
                _addFilesCancellation = null;
            }

            cancellation.Dispose();
            IsAddingFiles = false;
            UpdateCurrentFileCommands();
        }
    }

    private void RemoveCurrentFile_Executed(object? arg)
    {
        var item = CurrentFile;

        if (item == null || IsCurrentFileReferenced)
        {
            return;
        }

        CurrentFile = null;
        Delete_Executed(item);
    }

    private void LinkCurrentToQuestion_Executed(object? arg)
    {
        if (CurrentFile != null && _document.LinkMediaReferenceToActiveQuestion(this, CurrentFile))
        {
            RefreshReferenceState();
        }
    }

    internal void RefreshReferenceState()
    {
        OnPropertyChanged(nameof(IsCurrentFileReferenced));
        UpdateCurrentFileCommands();
    }

    internal void RefreshActiveQuestionState() => UpdateCurrentFileCommands();

    private void UpdateCurrentFileCommands()
    {
        AddFiles.CanBeExecuted = !_isAddingFiles;
        RemoveCurrentFile.CanBeExecuted = CanRemoveCurrentFile;
        LinkCurrentToQuestion.CanBeExecuted = CanLinkCurrentToQuestion;
        OnPropertyChanged(nameof(HasCurrentFile));
        OnPropertyChanged(nameof(IsCurrentFileReferenced));
        OnPropertyChanged(nameof(CanRemoveCurrentFile));
        OnPropertyChanged(nameof(CanLinkCurrentToQuestion));
    }

    private void CompressItem_Executed(object? arg)
    {
        if (arg == null)
        {
            return;
        }

        var item = (MediaItemViewModel)arg;

        if (item.MediaSource == null)
        {
            return;
        }

        try
        {
            var sourceUri = item.MediaSource.Uri;
            var newUri = PlatformManager.Instance.CompressImage(sourceUri);

            if (newUri != sourceUri)
            {
                var newItem = CreateItem(item.Name);
                var currentIndex = Files.IndexOf(item);

                var returnToCurrent = CurrentFile == item;

                PreviewRemove(item);
                PreviewAdd(newItem, newUri, currentIndex);
                HasPendingChanges = IsChanged();

                if (returnToCurrent)
                {
                    CurrentFile = newItem;
                }

                OnChanged(new CustomChange(
                    () =>
                    {
                        var returnToCurrent = CurrentFile == newItem;

                        PreviewRemove(newItem);
                        PreviewAdd(item, sourceUri);
                        HasPendingChanges = IsChanged();

                        if (returnToCurrent)
                        {
                            CurrentFile = item;
                        }
                    },
                    () =>
                    {
                        var returnToCurrent = CurrentFile == item;

                        PreviewRemove(item);
                        PreviewAdd(newItem, newUri);
                        HasPendingChanges = IsChanged();

                        if (returnToCurrent)
                        {
                            CurrentFile = newItem;
                        }
                    }));
            }
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    private void NavigateToUsage_Executed(object? arg)
    {
        if (arg == null)
        {
            return;
        }

        var item = (MediaItemViewModel)arg;
        var logo = _document.Package.Model.LogoItem;

        if (logo != null && item.Name == logo.Value)
        {
            _document.Navigate.Execute(_document.Package);
            return;
        }

        foreach (var round in _document.Package.Rounds)
        {
            foreach (var theme in round.Themes)
            {
                foreach (var question in theme.Questions)
                {
                    foreach (var content in question.Model.GetContent())
                    {
                        if (content.Value == item.Name)
                        {
                            _document.Navigate.Execute(question);
                            return;
                        }
                    }
                }
            }
        }
    }

    private void PreviewRemove(MediaItemViewModel item)
    {
        if (!Files.Contains(item))
        {
            return;
        }

        // Clean up event subscriptions
        item.Model.PropertyChanged -= Named_PropertyChanged;
        item.PropertyChanged -= MediaItem_PropertyChanged;

        if (_added.Contains(item))
        {
            _added.Remove(item);
            _removedStreams.Add(item, _streams[item]);
            _streams.Remove(item);
        }
        else
        {
            _removed.Add(item);
        }

        Files.Remove(item);
        OnPropertyChanged(nameof(Files));
    }

    public async Task CommitAsync(DataCollection collection, CancellationToken cancellationToken = default)
    {
        foreach (var item in _removed.ToArray())
        {
            collection.RemoveFile(item.Model.Name);
            item.Model.PropertyChanged -= Named_PropertyChanged;
            item.PropertyChanged -= MediaItem_PropertyChanged;
            _removed.Remove(item);
        }

        foreach (var item in _removedStreams)
        {
            item.Value.Dispose();
        }
        _removedStreams.Clear();

        foreach (var item in _added.ToArray())
        {
            var pendingFile = _streams[item];

            try
            {
                await collection.AddFileAsync(item.Model.Name, pendingFile.Stream, cancellationToken);
                pendingFile.Dispose();
                _added.Remove(item);
                _streams.Remove(item);
            }
            catch (Exception exc)
            {
                if (pendingFile.Stream.CanSeek)
                {
                    pendingFile.Stream.Position = 0;
                }

                OnError(exc);
            }
        }

        foreach (var item in _renamed.ToArray())
        {
            await collection.RenameFileAsync(item.Item1, item.Item2, cancellationToken);
            _renamed.Remove(item);
        }

        HasPendingChanges = IsChanged();
    }

    public async Task ApplyToAsync(
        DataCollection collection,
        bool final = false,
        CancellationToken cancellationToken = default)
    {
        foreach (var item in _removed.ToArray())
        {
            collection.RemoveFile(item.Model.Name);
        }

        foreach (var item in _added.ToArray())
        {
            var pendingFile = _streams[item];

            try
            {
                await collection.AddFileAsync(item.Model.Name, pendingFile.Stream, cancellationToken);
            }
            finally
            {
                if (pendingFile.Stream.CanSeek)
                {
                    pendingFile.Stream.Position = 0;
                }
            }
        }

        foreach (var item in _renamed.ToArray())
        {
            await collection.RenameFileAsync(item.Item1, item.Item2, cancellationToken);
        }

        if (final)
        {
            foreach (var pendingFile in _streams.Values
                .Concat(_removedStreams.Values)
                .Distinct())
            {
                pendingFile.Dispose();
            }

            _streams.Clear();
            _removedStreams.Clear();
            _added.Clear();
            _removed.Clear();
            _renamed.Clear();
        }
    }

    /// <summary>
    /// Accepts media changes after a fully validated package has been committed.
    /// </summary>
    internal void AcceptPendingChanges()
    {
        foreach (var item in _removed)
        {
            item.Model.PropertyChanged -= Named_PropertyChanged;
            item.PropertyChanged -= MediaItem_PropertyChanged;
        }

        foreach (var streamInfo in _removedStreams.Values)
        {
            streamInfo.Dispose();
        }

        foreach (var item in _added)
        {
            if (_streams.Remove(item, out var streamInfo))
            {
                streamInfo.Dispose();
            }
        }

        _removedStreams.Clear();
        _added.Clear();
        _removed.Clear();
        _renamed.Clear();
        HasPendingChanges = false;
    }

    private void AddItem_Executed(object? arg)
    {
        var multiselect = arg is not bool b || b;
        var files = PlatformManager.Instance.ShowMediaOpenUI(_name, !_document.Package.HasQualityControl, multiselect);

        if (files == null)
        {
            return;
        }

        foreach (var file in files)
        {
            try
            {
                AddFile(file);
            }
            catch (Exception exc)
            {
                OnError(exc);
            }
        }

        HasPendingChanges = IsChanged();
    }

    public MediaItemViewModel AddFile(string file, string? name = null)
        => AddFileCore(file, name, deleteSourceOnRelease: false);

    internal async Task<StagedMediaFile> StageFileAsync(
        PickedFile pickedFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pickedFile);
        cancellationToken.ThrowIfCancellationRequested();

        var safeName = GetSafePickedFileName(pickedFile);

        if (!string.IsNullOrWhiteSpace(pickedFile.LocalPath))
        {
            return new StagedMediaFile(
                Path.GetFullPath(pickedFile.LocalPath),
                safeName,
                deleteOnDispose: false,
                _logger);
        }

        Directory.CreateDirectory(_temporaryMediaDirectory);
        var extension = Path.GetExtension(safeName);
        var temporaryPath = Path.Combine(
            _temporaryMediaDirectory,
            $"import-{Guid.NewGuid():N}{extension}");

        try
        {
            await using var source = await pickedFile.OpenReadAsync(cancellationToken);
            await using var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
            target.Flush(flushToDisk: true);
            return new StagedMediaFile(temporaryPath, safeName, deleteOnDispose: true, _logger);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath, _logger);
            throw;
        }
    }

    internal MediaItemViewModel AddFile(StagedMediaFile stagedFile)
    {
        ArgumentNullException.ThrowIfNull(stagedFile);

        var item = AddFileCore(stagedFile.Path, stagedFile.DisplayName, stagedFile.DeleteOnDispose);
        stagedFile.TransferOwnership();
        return item;
    }

    private MediaItemViewModel AddFileCore(string file, string? name, bool deleteSourceOnRelease)
    {
        if (_document.Package.HasQualityControl)
        {
            ValidateFileExtensionAndSize(file);
        }

        var localName = name ?? Path.GetFileName(file).Replace("%", "");
        var uniqueName = FileHelper.GenerateUniqueFileName(localName, name => Files.Any(f => f.Model.Name == name));

        var item = CreateItem(uniqueName);
        PreviewAdd(item, file, deleteSourceOnRelease: deleteSourceOnRelease);

        OnChanged(new CustomChange(
            () =>
            {
                PreviewRemove(item);
                HasPendingChanges = IsChanged();
            },
            () =>
            {
                PreviewAdd(item, file, deleteSourceOnRelease: deleteSourceOnRelease);
                HasPendingChanges = IsChanged();
            }));

        return item;
    }

    private static string GetSafePickedFileName(PickedFile pickedFile)
    {
        var safeName = Path.GetFileName(pickedFile.DisplayName).Replace("%", "");
        var extension = Path.GetExtension(safeName);

        if (extension.Length == 0)
        {
            var declaredExtension = pickedFile.Extension.StartsWith('.')
                ? pickedFile.Extension
                : $".{pickedFile.Extension}";
            extension = Path.GetExtension($"file{declaredExtension}");
        }

        if (string.IsNullOrWhiteSpace(safeName))
        {
            return $"media{extension}";
        }

        return Path.GetExtension(safeName).Length == 0 ? $"{safeName}{extension}" : safeName;
    }

    private void ValidateFileExtensionAndSize(string fileName)
    {
        var fileExtensions = Quality.FileExtensions[_name];
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (!fileExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                string.Format(
                    Resources.InvalidFileExtension,
                    fileName,
                    extension,
                    string.Join(", ", fileExtensions)));
        }

        var maximumSize = Quality.FileSizeMb[_name];

        if (new FileInfo(fileName).Length > maximumSize * 1024 * 1024)
        {
            throw new InvalidOperationException(
                string.Format(
                    Resources.InvalidFileSize,
                    fileName,
                    maximumSize));
        }
    }

    private void PreviewAdd(
        MediaItemViewModel item,
        string path,
        int index = -1,
        bool deleteSourceOnRelease = false)
    {
        if (_removed.Contains(item))
        {
            _removed.Remove(item);
        }
        else if (_removedStreams.Remove(item, out var removedStream))
        {
            _added.Add(item);
            _streams[item] = removedStream;
        }
        else
        {
            FileStream fileStream;

            try
            {
                fileStream = File.OpenRead(path);
            }
            catch (Exception exc)
            {
                _logger.LogWarning(exc, "PreviewAdd error: {error}", exc.Message);
                throw;
            }

            _added.Add(item);
            _streams[item] = new PendingMediaFile(path, fileStream, deleteSourceOnRelease, _logger);
        }

        AttachItem(item);

        if (index == -1)
        {
            Files.Add(item);
        }
        else
        {
            Files.Insert(index, item);
        }

        OnPropertyChanged(nameof(Files));

        HasPendingChanges = IsChanged();
    }

    internal long GetLength(string link) =>
        _document.Lock.WithLock(
            () =>
            {
                var collection = _document.GetInternalCollection(_name);
                return collection.GetFileLength(link);
            });

    /// <summary>
    /// Captures a framework-neutral stream factory for controlled question preview without materializing a platform path.
    /// </summary>
    internal QuestionPreviewMediaSource CreateQuestionPreviewSource(
        QuestionPreviewMediaKind kind,
        string link)
    {
        var requestedLink = link;

        // Preserve the established pending-rename lookup semantics while capturing an immutable source factory.
        foreach (var item in _renamed)
        {
            if (item.Item2 == link)
            {
                link = item.Item1;
                break;
            }
        }

        var pendingStream = _streams.FirstOrDefault(item => item.Key.Model.Name == link);
        if (pendingStream.Key is not null)
        {
            var path = pendingStream.Value.Path;
            return new QuestionPreviewMediaSource(
                kind,
                requestedLink,
                () =>
                {
                    var file = new FileInfo(path);
                    return file.Exists
                        ? new QuestionPreviewMediaStream(File.OpenRead(file.FullName), file.Length)
                        : null;
                });
        }

        var storageLink = link;
        return new QuestionPreviewMediaSource(
            kind,
            requestedLink,
            () => _document.Lock.WithLock(
                () =>
                {
                    var collection = _document.GetInternalCollection(_name);
                    var streamInfo = collection.GetFile(storageLink);
                    return streamInfo is null
                        ? null
                        : new QuestionPreviewMediaStream(streamInfo.Stream, streamInfo.Length);
                }));
    }

    // TODO: switch from IMedia to MediaInfo struct
    internal IMedia Wrap(string link)
    {
        // TODO: not a very effective way of handling pending renamed files, but other ways are more complex
        foreach (var item in _renamed)
        {
            if (item.Item2 == link)
            {
                link = item.Item1;
                break;
            }    
        }

        var pendingStream = _streams.FirstOrDefault(n => n.Key.Model.Name == link);

        if (pendingStream.Key != null)
        {
            return new Media(pendingStream.Value.Path, pendingStream.Value.Stream.Length);
        }

        return _document.Lock.WithLock(
            () =>
            {
                var collection = _document.GetInternalCollection(_name); // This value cannot be cached as internal collection link can eventually change 
                
                return PlatformManager.Instance.PrepareMedia(
                    new Media(() => collection.GetFile(link), () => collection.GetFileLength(link), _internalId + link),
                    collection.Name);
            });
    }

    internal StreamInfo? TryGetStreamInfo(string link)
    {
        foreach (var item in _renamed)
        {
            if (item.Item2 == link)
            {
                link = item.Item1;
                break;
            }
        }

        var pendingStream = _streams.FirstOrDefault(n => n.Key.Model.Name == link);

        if (pendingStream.Key != null)
        {
            var fileInfo = new FileInfo(pendingStream.Value.Path);
            return new StreamInfo(File.OpenRead(fileInfo.FullName), fileInfo.Length);
        }

        return _document.Lock.WithLock(
            () =>
            {
                var collection = _document.GetInternalCollection(_name);
                return collection.GetFile(link);
            });
    }

    /// <summary>
    /// Tries to get global file path for a media file.
    /// </summary>
    /// <param name="mediaItem">Media file.</param>
    /// <returns>Global file path for media file or null.</returns>
    internal string? TryGetFilePath(MediaItemViewModel mediaItem)
    {
        var pendingStream = _streams.FirstOrDefault(n => n.Key.Model.Name == mediaItem.Model.Name);

        if (pendingStream.Key != null)
        {
            return pendingStream.Value.Path;
        }

        return null;
    }

    /// <summary>
    /// Tries to get stream for a media file.
    /// </summary>
    /// <param name="mediaItem">Media file.</param>
    internal Stream? TryGetStream(MediaItemViewModel mediaItem)
    {
        var collection = _document.GetInternalCollection(_name);
        var streamInfo = collection.GetFile(mediaItem.Model.Name);
        return streamInfo?.Stream;
    }

    internal StorageChanges GetChanges() => new()
    {
        Added = _added.Select(item => _streams[item].Path).ToArray(),
        Removed = _removed.Select(item => item.Model.Name).ToArray(),
        Renamed = _renamed.ToDictionary(item => item.Item1, item => item.Item2)
    };

    internal void RestoreChanges(StorageChanges storageChanges)
    {
        foreach (var item in storageChanges.Added)
        {
            AddFile(item);
        }

        foreach (var item in storageChanges.Removed)
        {
            var mediaItem = Files.FirstOrDefault(f => f.Model.Name == item);

            if (mediaItem != null)
            {
                PreviewRemove(mediaItem);
            }
        }

        foreach (var item in storageChanges.Renamed)
        {
            _renamed.Add(Tuple.Create(item.Key, item.Value));
        }
    }

    /// <summary>
    /// Applies current sorting to the Files collection.
    /// </summary>
    private void ApplySorting()
    {
        if (Files.Count == 0)
        {
            return;
        }

        var sortedFiles = _sortField switch
        {
            MediaSortField.Name => _sortDirection == SortDirection.Ascending
                ? Files.OrderBy(f => f.Model.Name).ToList()
                : Files.OrderByDescending(f => f.Model.Name).ToList(),
            MediaSortField.Size => _sortDirection == SortDirection.Ascending
                ? Files.OrderBy(f => GetMediaSize(f)).ToList()
                : Files.OrderByDescending(f => GetMediaSize(f)).ToList(),
            _ => Files.ToList()
        };

        // Clear and refill to maintain proper collection change notifications
        var selectedFile = CurrentFile;
        Files.Clear();
        
        foreach (var file in sortedFiles)
        {
            Files.Add(file);
        }

        // Restore selection if it was set
        if (selectedFile != null && Files.Contains(selectedFile))
        {
            CurrentFile = selectedFile;
        }
    }

    /// <summary>
    /// Gets the size of media item, with fallback for pending streams and file system lookup.
    /// </summary>
    /// <param name="mediaItem">Media item to get size for.</param>
    /// <returns>Size in bytes, or 0 if not available.</returns>
    private long GetMediaSize(MediaItemViewModel mediaItem)
    {
        // First try to get size from loaded MediaSource
        if (mediaItem.MediaSource != null)
        {
            return mediaItem.MediaSource.StreamLength;
        }

        // Try to get size from pending stream
        var pendingStream = _streams.FirstOrDefault(s => s.Key == mediaItem);
        if (pendingStream.Key != null)
        {
            return pendingStream.Value.Stream.Length;
        }

        // Fallback: try to get size using the document's GetLength method
        try
        {
            return GetLength(mediaItem.Model.Name);
        }
        catch
        {
            // If all else fails, return 0
            return 0;
        }
    }

    private static void TryDeleteTemporaryFile(string path, ILogger logger)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exc)
        {
            logger.LogWarning(exc, "Could not delete staged media file {path}", path);
        }
    }

    internal sealed class StagedMediaFile : IDisposable
    {
        private readonly ILogger _logger;
        private bool _ownsFile;

        internal string Path { get; }

        internal string DisplayName { get; }

        internal bool DeleteOnDispose { get; }

        internal StagedMediaFile(
            string path,
            string displayName,
            bool deleteOnDispose,
            ILogger logger)
        {
            Path = path;
            DisplayName = displayName;
            DeleteOnDispose = deleteOnDispose;
            _ownsFile = deleteOnDispose;
            _logger = logger;
        }

        internal void TransferOwnership() => _ownsFile = false;

        public void Dispose()
        {
            if (_ownsFile)
            {
                _ownsFile = false;
                TryDeleteTemporaryFile(Path, _logger);
            }
        }
    }

    private sealed class PendingMediaFile : IDisposable
    {
        private readonly bool _deleteOnDispose;
        private readonly ILogger _logger;
        private bool _isDisposed;

        internal string Path { get; }

        internal FileStream Stream { get; }

        internal PendingMediaFile(string path, FileStream stream, bool deleteOnDispose, ILogger logger)
        {
            Path = path;
            Stream = stream;
            _deleteOnDispose = deleteOnDispose;
            _logger = logger;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            Stream.Dispose();

            if (_deleteOnDispose)
            {
                TryDeleteTemporaryFile(Path, _logger);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _addFilesCancellation?.Cancel();

            foreach (var item in Files.Concat(_removed).Distinct())
            {
                item.Model.PropertyChanged -= Named_PropertyChanged;
                item.PropertyChanged -= MediaItem_PropertyChanged;
            }

            foreach (var pendingFile in _streams.Values
                .Concat(_removedStreams.Values)
                .Distinct())
            {
                pendingFile.Dispose();
            }

            _streams.Clear();
            _removedStreams.Clear();
        }

        base.Dispose(disposing);
    }
}
