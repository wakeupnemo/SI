using SIPackages;
using SIPackages.Core;
using SIPackages.Models;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.PlatformSpecific;
using SIQuester.ViewModel.Properties;
using SIQuester.ViewModel.Services;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Defines a content item list view model.
/// </summary>
public sealed class ContentItemsViewModel : ItemsViewModel<ContentItemViewModel>, IContentCollection
{
    private List<ContentItem> Model { get; }

    public QuestionViewModel Owner { get; private set; }

    public override QDocument? OwnerDocument => Owner?.OwnerTheme?.OwnerRound?.OwnerPackage?.Document;

    public ICommand AddText { get; private set; }

    public ICommand AddVoice { get; private set; }

    public ICommand ChangePlacement { get; private set; }

    public SimpleCommand SetTime { get; private set; }

    /// <summary>
    /// Joins content item with next one to play them together.
    /// </summary>
    public SimpleCommand JoinWithNext { get; private set; }

    /// <summary>
    /// Gets a read-only, transient grouping of canonical content items into presentation moments.
    /// </summary>
    public ReadOnlyObservableCollection<ContentMomentViewModel> Moments { get; }

    private readonly ObservableCollection<ContentMomentViewModel> _moments = new();

    private bool _suppressMomentRefresh;

    private ContentItemViewModel? _observedCurrentItem;

    public SimpleCommand CollapseMedia { get; private set; }

    public SimpleCommand ExpandMedia { get; private set; }

    public SimpleCommand ExportMedia { get; private set; }

    /// <summary>
    /// Navigates to media file in media collection.
    /// </summary>
    public SimpleCommand NavigateToFile { get; private set; }

    public ICommand LinkFile { get; private set; }

    public SimpleCommand LinkUri { get; private set; }

    /// <summary>
    /// Adds package-owned media through the platform-neutral picker.
    /// </summary>
    public AsyncCommand AddFile { get; }

    ICommand IContentCollection.AddFile => AddFile;

    private bool _isAddingMediaFiles;

    /// <summary>
    /// Gets whether this content collection currently owns a media picker operation.
    /// </summary>
    public bool IsAddingMediaFiles
    {
        get => _isAddingMediaFiles;
        private set
        {
            if (_isAddingMediaFiles == value)
            {
                return;
            }

            _isAddingMediaFiles = value;
            AddFile.CanBeExecuted = !value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsAddingMediaFiles)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(CanAddMediaFiles)));
        }
    }

    /// <summary>
    /// Gets whether a new media-picker operation may be started for this collection.
    /// </summary>
    public bool CanAddMediaFiles => !_isAddingMediaFiles;

    public bool IsTopLevel { get; private set; }

    public ContentItemsViewModel(QuestionViewModel question, List<ContentItem> contentItems, bool isTopLevel)
    {
        Owner = question;
        Model = contentItems;
        IsTopLevel = isTopLevel;
        Moments = new ReadOnlyObservableCollection<ContentMomentViewModel>(_moments);

        foreach (var item in contentItems)
        {
            Add(new ContentItemViewModel(item) { Owner = this });
        }

        CollectionChanged += ContentItemsViewModel_CollectionChanged;

        AddText = new SimpleCommand(AddScreenText_Executed);
        AddVoice = new SimpleCommand(AddReplicText_Executed);

        ChangePlacement = new SimpleCommand(ChangePlacement_Executed);

        SetTime = new SimpleCommand(SetTime_Executed);
        JoinWithNext = new SimpleCommand(JoinWithNext_Executed);

        CollapseMedia = new SimpleCommand(CollapseMedia_Executed);
        ExpandMedia = new SimpleCommand(ExpandMedia_Executed);
        ExportMedia = new SimpleCommand(ExportMedia_Executed);
        NavigateToFile = new SimpleCommand(NavigateToFile_Executed);

        LinkFile = new SimpleCommand(LinkFile_Executed);
        LinkUri = new SimpleCommand(LinkUri_Executed);
        AddFile = new AsyncCommand(AddFile_ExecutedAsync);
        IsTopLevel = isTopLevel;
        ObserveCurrentItem(CurrentItem);
        RefreshMoments();
    }

    internal void SelectItem(ContentItemViewModel item) => CurrentItem = item;

    internal void SeparateAfter(ContentItemViewModel item)
    {
        if (!Contains(item) || item.Model.WaitForFinish)
        {
            return;
        }

        item.Model.WaitForFinish = true;
        CurrentItem = item;
    }

    internal void MergeWithNext(ContentMomentViewModel moment)
    {
        var momentIndex = _moments.IndexOf(moment);

        if (momentIndex < 0 || momentIndex + 1 >= _moments.Count || moment.Items.Count == 0)
        {
            return;
        }

        var boundaryItem = moment.Items[^1];
        boundaryItem.Model.WaitForFinish = false;
        CurrentItem = boundaryItem;
    }

    internal void MoveMoment(ContentMomentViewModel moment, int offset)
    {
        var sourceIndex = _moments.IndexOf(moment);
        var targetIndex = sourceIndex + offset;

        if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= _moments.Count)
        {
            return;
        }

        var document = OwnerDocument;

        if (document == null)
        {
            return;
        }

        try
        {
            var groups = _moments.Select(item => item.Items.ToArray()).ToList();
            (groups[sourceIndex], groups[targetIndex]) = (groups[targetIndex], groups[sourceIndex]);
            var reorderedItems = groups.SelectMany(item => item).ToArray();
            var selectedItem = CurrentItem;

            using var change = document.OperationsManager.BeginComplexChange();
            _suppressMomentRefresh = true;

            try
            {
                for (var i = 0; i < reorderedItems.Length; i++)
                {
                    this[i] = reorderedItems[i];
                }

                for (var i = 0; i + 1 < groups.Count; i++)
                {
                    groups[i][^1].Model.WaitForFinish = true;
                }
            }
            finally
            {
                _suppressMomentRefresh = false;
                RefreshMoments();
            }

            CurrentPosition = selectedItem == null ? -1 : IndexOf(selectedItem);
            change.Commit();
        }
        catch (Exception exc)
        {
            document.OnError(exc);
        }
    }

    /// <summary>
    /// Rebuilds the transient moment projection after canonical grouping or placement changes.
    /// </summary>
    internal void RefreshMoments()
    {
        if (_suppressMomentRefresh)
        {
            return;
        }

        _moments.Clear();

        var momentItems = new List<ContentItemViewModel>();
        var groupedMoments = new List<IReadOnlyList<ContentItemViewModel>>();

        foreach (var item in this)
        {
            momentItems.Add(item);

            if (item.Model.WaitForFinish)
            {
                groupedMoments.Add(momentItems.ToArray());
                momentItems.Clear();
            }
        }

        if (momentItems.Count > 0)
        {
            groupedMoments.Add(momentItems.ToArray());
        }

        for (var i = 0; i < groupedMoments.Count; i++)
        {
            _moments.Add(new ContentMomentViewModel(this, i + 1, groupedMoments[i], i + 1 < groupedMoments.Count));
        }
    }

    internal void AddScreenText_Executed(object? arg)
    {
        CurrentItem = Add(ContentTypes.Text, "", ContentPlacements.Screen);
        QDocument.ActivatedObject = CurrentItem;
    }

    private void AddReplicText_Executed(object? arg)
    {
        var index = CurrentPosition;

        if (index > -1 && index < Count && string.IsNullOrWhiteSpace(this[index].Model.Value))
        {
            RemoveAt(index);
        }

        CurrentItem = Add(ContentTypes.Text, "", ContentPlacements.Replic);
        QDocument.ActivatedObject = CurrentItem;
    }

    private void ChangePlacement_Executed(object? arg)
    {
        var index = CurrentPosition;

        if (index > -1 && index < Count)
        {
            var contentItem = this[index];

            if (contentItem.Type != ContentTypes.Text)
            {
                return;
            }

            if (contentItem.Model.Placement == ContentPlacements.Screen)
            {
                contentItem.Model.Placement = ContentPlacements.Replic;
            }
            else if (contentItem.Model.Placement == ContentPlacements.Replic)
            {
                contentItem.Model.Placement = ContentPlacements.Screen;
            }
        }
    }

    internal ContentItemViewModel Add(string contentType, string value, string placement)
    {
        var contentItem = new ContentItemViewModel(new ContentItem { Type = contentType, Value = value, Placement = placement });
        Add(contentItem);

        if (!IsTopLevel)
        {
            while (Count > 1)
            {
                RemoveAt(0);
            }
        }

        return contentItem;
    }

    protected override void OnCurrentItemChanged(ContentItemViewModel? oldValue, ContentItemViewModel? newValue)
    {
        base.OnCurrentItemChanged(oldValue, newValue);

        ObserveCurrentItem(newValue);
    }

    private void ObserveCurrentItem(ContentItemViewModel? item)
    {
        if (ReferenceEquals(_observedCurrentItem, item))
        {
            item?.SetCurrent(true);
            UpdateContentItemCommands();
            return;
        }

        if (_observedCurrentItem != null)
        {
            _observedCurrentItem.SetCurrent(false);
            _observedCurrentItem.PropertyChanged -= CurrentAtom_PropertyChanged;
            _observedCurrentItem.Model.PropertyChanged -= Model_PropertyChanged;
        }

        _observedCurrentItem = item;

        if (_observedCurrentItem != null)
        {
            _observedCurrentItem.SetCurrent(true);
            _observedCurrentItem.PropertyChanged += CurrentAtom_PropertyChanged;
            _observedCurrentItem.Model.PropertyChanged += Model_PropertyChanged;
        }

        UpdateContentItemCommands();
    }

    private void SynchronizeCurrentSelection()
    {
        var currentItem = CurrentItem;

        if (currentItem != null)
        {
            var actualPosition = IndexOf(currentItem);

            // A moment move is recorded as a series of replacements. During undo/redo the selected item can be
            // temporarily absent, so retain its identity until the replacement series puts it back.
            if (actualPosition >= 0 && actualPosition != CurrentPosition)
            {
                CurrentPosition = actualPosition;
            }
        }

        ObserveCurrentItem(CurrentItem);
    }

    protected override bool CanRemove() => Count > 1 || 
        Count == 1 && (this[0].Model.Type != ContentTypes.Text ||
            this[0].Model.Placement != ContentPlacements.Screen ||
            this[0].Model.Value.Length > 0);

    private void CurrentAtom_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContentItemViewModel.IsExpanded))
        {
            UpdateContentItemCommands();
        }
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContentItem.Duration))
        {
            UpdateContentItemCommands();
        }
    }

    private void ContentItemsViewModel_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (int i = e.NewStartingIndex; i < e.NewStartingIndex + e.NewItems?.Count; i++)
                {
                    this[i].Owner = this;
                    Model.Insert(i, this[i].Model);
                }

                break;

            case NotifyCollectionChangedAction.Replace:
                for (int i = e.NewStartingIndex; i < e.NewStartingIndex + e.NewItems?.Count; i++)
                {
                    this[i].Owner = this;
                    Model[i] = this[i].Model;
                }

                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (var contentItem in e.OldItems.Cast<ContentItemViewModel>())
                    {
                        contentItem.Owner = null;
                        Model.RemoveAt(e.OldStartingIndex);
                        OwnerDocument?.ClearLinks(contentItem.Model);
                    }
                }

                break;

            case NotifyCollectionChangedAction.Reset:
                Model.Clear();

                foreach (ContentItemViewModel contentItem in this)
                {
                    contentItem.Owner = this;
                    Model.Add(contentItem.Model);
                }

                break;
        }

        SynchronizeCurrentSelection();
        UpdateCommands();
        if (!_suppressMomentRefresh)
        {
            RefreshMoments();
        }
    }

    protected override void OnRemoveAt(int index)
    {
        base.OnRemoveAt(index);

        if (Count == 0)
        {
            Add(new ContentItemViewModel(new ContentItem
            {
                Type = ContentTypes.Text,
                IsRef = false,
                Value = "",
                Placement = ContentPlacements.Screen
            }));
        }
    }

    private void UpdateContentItemCommands()
    {
        var contentItem = CurrentItem;
        var contentType = contentItem?.Model.Type;

        var isMedia = contentType == ContentTypes.Image
            || contentType == ContentTypes.Audio
            || contentType == ContentTypes.Video
            || contentType == ContentTypes.Html;

        SetTime.CanBeExecuted = contentItem != null && contentItem.Model.Duration == TimeSpan.Zero;

        CollapseMedia.CanBeExecuted = contentItem != null && isMedia && contentItem.IsExpanded;
        ExpandMedia.CanBeExecuted = contentItem != null && isMedia && !contentItem.IsExpanded;
        ExportMedia.CanBeExecuted = contentItem != null && isMedia;
        NavigateToFile.CanBeExecuted = contentItem != null && isMedia;
    }

    private void SetTime_Executed(object? arg)
    {
        if (CurrentItem == null)
        {
            return;
        }

        QDocument.ActivatedObject = CurrentItem;
        CurrentItem.Model.Duration = TimeSpan.FromSeconds(5);
    }

    private void JoinWithNext_Executed(object? arg)
    {
        if (CurrentItem == null)
        {
            return;
        }

        CurrentItem.Model.WaitForFinish = !CurrentItem.Model.WaitForFinish;
    }

    private void CollapseMedia_Executed(object? arg)
    {
        if (CurrentItem == null)
        {
            return;
        }

        CurrentItem.IsExpanded = false;
        CollapseMedia.CanBeExecuted = false;
        ExpandMedia.CanBeExecuted = true;
    }

    private void ExpandMedia_Executed(object? arg)
    {
        if (CurrentItem == null)
        {
            return;
        }

        CurrentItem.IsExpanded = true;
        CollapseMedia.CanBeExecuted = true;
        ExpandMedia.CanBeExecuted = false;
    }

    private void ExportMedia_Executed(object? arg)
    {
        if (CurrentItem == null)
        {
            return;
        }

        try
        {
            var document = OwnerDocument ?? throw new InvalidOperationException("document is undefined");
            var media = document.Document.TryGetMedia(CurrentItem.Model);

            if (media.HasValue && media.Value.HasStream)
            {
                var fileName = CurrentItem.Model.Value;

                if (PlatformManager.Instance.ShowSaveUI(Resources.ExportMedia, "", null, ref fileName))
                {
                    using var stream = media.Value.Stream!;
                    using var fileStream = File.Open(fileName, FileMode.Create, FileAccess.Write);

                    stream.CopyTo(fileStream);
                }
            }
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.ShowExclamationMessage($"{Resources.ExportMediaError}: {exc}");
        }
    }

    private void NavigateToFile_Executed(object? arg)
    {
        try
        {
            var document = OwnerDocument ?? throw new InvalidOperationException("document is undefined");
            var collection = document.TryGetCollectionByMediaType(CurrentItem.Model.Type);

            if (collection == null)
            {
                return;
            }

            foreach (var file in collection.Files)
            {
                if (file.Name == CurrentItem.Model.Value)
                {
                    document.NavigateToStorageItem(collection, file);
                    return;
                }
            }
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.ShowExclamationMessage(exc.Message);
        }
    }

    private async Task AddFile_ExecutedAsync(object? arg)
    {
        var contentType = arg?.ToString();

        if (contentType == null || _isAddingMediaFiles)
        {
            return;
        }

        var document = OwnerDocument;

        if (document == null)
        {
            return;
        }

        MediaStorageViewModel collection;

        try
        {
            collection = document.GetCollectionByMediaType(contentType);
        }
        catch (ArgumentException exc)
        {
            document.OnError(exc);
            return;
        }

        IsAddingMediaFiles = true;
        var stagedFiles = new List<MediaStorageViewModel.StagedMediaFile>();
        var cancellationToken = document.LifetimeToken;

        try
        {
            var extensions = Quality.FileExtensions[collection.Name]
                .Select(extension => extension.TrimStart('.'))
                .ToArray();
            var pickedFiles = await document.FilePickerService.PickOpenFilesAsync(
                new OpenFilePickerRequest(
                    collection.Header,
                    [new FileTypeFilter(collection.Header, extensions)],
                    AllowMultiple: IsTopLevel),
                cancellationToken);

            if (pickedFiles.Count == 0)
            {
                return;
            }

            var filesToImport = (IsTopLevel ? pickedFiles : pickedFiles.Take(1)).ToArray();

            foreach (var pickedFile in filesToImport)
            {
                var classification = ExternalDropClassifier.Classify(pickedFile);

                if (!classification.IsMedia || classification.ContentType != contentType)
                {
                    var extension = Path.GetExtension(pickedFile.DisplayName);
                    throw new InvalidOperationException(string.Format(
                        Resources.InvalidFileExtension,
                        pickedFile.DisplayName,
                        extension,
                        string.Join(", ", Quality.FileExtensions[collection.Name])));
                }
            }

            foreach (var pickedFile in filesToImport)
            {
                stagedFiles.Add(await collection.StageFileAsync(pickedFile, cancellationToken));
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var change = document.OperationsManager.BeginComplexChange();
            var addedFiles = stagedFiles.Select(collection.AddFile).ToArray();
            InsertMediaReferences(document, contentType, addedFiles);
            change.Commit();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exc)
        {
            document.OnError(exc);
        }
        finally
        {
            foreach (var stagedFile in stagedFiles)
            {
                stagedFile.Dispose();
            }

            IsAddingMediaFiles = false;
        }
    }

    private void InsertMediaReferences(
        QDocument document,
        string contentType,
        IReadOnlyList<MediaItemViewModel> files)
    {
        var index = CurrentPosition;

        if (Count > 0)
        {
            if (index < 0 || index >= Count)
            {
                index = Count - 1;
            }

            if (string.IsNullOrWhiteSpace(this[index].Model.Value))
            {
                RemoveAt(index--);
            }
        }
        else
        {
            index = -1;
        }

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            var file = files[fileIndex];
            Insert(index + 1 + fileIndex, new ContentItemViewModel(new ContentItem
            {
                Type = contentType,
                IsRef = true,
                Value = file.Model.Name,
                Placement = contentType == ContentTypes.Audio
                    ? ContentPlacements.Background
                    : ContentPlacements.Screen,
                Duration = document.GetDurationByContentType(contentType),
            }));

            if (AppSettings.Default.SetRightAnswerFromFileName)
            {
                Owner.TryAddRightAnswerFromFileName(file.Model.Name);
            }
        }

        if (!IsTopLevel)
        {
            while (Count > 1)
            {
                RemoveAt(0);
            }
        }

        document.ActiveItem = null;
    }

    private void LinkUri_Executed(object? arg)
    {
        var contentType = arg?.ToString();

        if (contentType == null)
        {
            return;
        }

        try
        {
            LinkContentUri(contentType);
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.ShowExclamationMessage(exc.Message);
        }
    }

    private void LinkFile_Executed(object? arg)
    {
        if (arg is not (MediaItemViewModel file, string contentType))
        {
            return;
        }

        try
        {
            LinkExistingContentFile(contentType, file);
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.ShowExclamationMessage(exc.Message);
        }
    }

    public void TryImportMedia(string filePath)
    {
        var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();

        foreach (var item in Quality.FileExtensions)
        {
            if (item.Value.Contains(fileExtension))
            {
                TryImportMedia(filePath, item.Key);
                break;
            }
        }
    }

    public void TryImportMedia(string filePath, string mediaType)
    {
        var contentType = CollectionNames.TryGetContentType(mediaType);

        if (contentType == null)
        {
            return;
        }

        try
        {
            var document = OwnerDocument ?? throw new InvalidOperationException("document is undefined");

            var collection = document.GetCollection(mediaType);
            var item = collection.AddFile(filePath);

            if (Count > 0 && string.IsNullOrWhiteSpace(this[^1].Model.Value))
            {
                RemoveAt(Count - 1);
            }

            Add(new ContentItemViewModel(new ContentItem
            {
                Type = contentType,
                IsRef = true,
                Value = item.Model.Name,
                Placement = contentType == ContentTypes.Audio ? ContentPlacements.Background : ContentPlacements.Screen
            }));

            if (AppSettings.Default.SetRightAnswerFromFileName)
            {
                Owner.TryAddRightAnswerFromFileName(item.Model.Name);
            }
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.ShowErrorMessage(exc.Message);
        }
    }

    private bool LinkContentUri(string contentType)
    {
        var document = OwnerDocument ?? throw new InvalidOperationException("document is undefined");
        var index = CurrentPosition;

        if (index == -1 || index >= Count)
        {
            index = Count - 1;
        }

        var uri = PlatformManager.Instance.AskText(Resources.InputMediaUri);

        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        try
        {
            using var change = document.OperationsManager.BeginComplexChange();

            if (index > -1 && index < Count && string.IsNullOrWhiteSpace(this[index].Model.Value))
            {
                RemoveAt(index--);
            }

            var contentItemViewModel = new ContentItemViewModel(new ContentItem
            {
                Type = contentType,
                Value = uri,
                Placement = contentType == ContentTypes.Audio ? ContentPlacements.Background : ContentPlacements.Screen,
                Duration = document.GetDurationByContentType(contentType),
            });

            QDocument.ActivatedObject = contentItemViewModel;
            Insert(index + 1, contentItemViewModel);
            document.ActiveItem = null;

            change.Commit();
            return true;
        }
        catch (Exception exc)
        {
            document.OnError(exc);
            return false;
        }
    }

    private void LinkExistingContentFile(string contentType, MediaItemViewModel file)
    {
        var document = OwnerDocument ?? throw new InvalidOperationException("document is undefined");
        var index = CurrentPosition;

        if (index == -1)
        {
            index = Count - 1;
        }

        try
        {
            using var change = document.OperationsManager.BeginComplexChange();

            if (string.IsNullOrWhiteSpace(this[index].Model.Value))
            {
                RemoveAt(index--);
            }

            var contentItemViewModel = new ContentItemViewModel(new ContentItem
            { 
                Type = contentType,
                Value = "",
                Placement = contentType == ContentTypes.Audio ? ContentPlacements.Background : ContentPlacements.Screen,
                Duration = document.GetDurationByContentType(contentType),
            });

            Insert(index + 1, contentItemViewModel);

            contentItemViewModel.Model.IsRef = true;
            contentItemViewModel.Model.Value = file.Model.Name;
            document.ActiveItem = null;

            change.Commit();
        }
        catch (Exception exc)
        {
            document.OnError(exc);
        }
    }

}
