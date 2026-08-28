using SIPackages;
using SIPackages.Core;
using SIPackages.Models;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Helpers;
using SIQuester.ViewModel.PlatformSpecific;
using SIQuester.ViewModel.Properties;
using SIQuester.ViewModel.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Defines a package view model.
/// </summary>
public sealed class PackageViewModel : ItemViewModel<Package>
{
    public override IItemViewModel? Owner => null;

    public QDocument Document { get; private set; }

    public ObservableCollection<RoundViewModel> Rounds { get; } = new();

    public ICommand AddRound { get; private set; }

    public SimpleCommand AddRestrictions { get; private set; }

    public SimpleCommand AddTags { get; private set; }

    public SimpleCommand ChangeLanguage { get; private set; }

    public TagsViewModel Tags { get; private set; }

    public override ICommand Add { get; protected set; }

    public override string AddHeader => Resources.AddRound;

    public override ICommand? Remove
    {
        get => null;
        protected set { }
    }

    /// <summary>
    /// Selects logo for package.
    /// </summary>
    public AsyncCommand SelectLogo { get; }

    public SimpleCommand RemoveLogo { get; }

    private bool _isLogoUpdatePending;

    private IMedia? _logo = null;

    public IMedia? Logo
    {
        get
        {
            if (_logo == null)
            {
                if (Model.Logo != null && Model.Logo.Length > 0)
                {
                    _logo = Document.Images.Wrap(Model.Logo[1..]);
                }
            }

            return _logo;
        }
        set
        {
            if (_logo != value)
            {
                _logo = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasLogo => !string.IsNullOrEmpty(Model.Logo);

    public string LogoName => Model.LogoItem?.Value ?? "";

    /// <summary>
    /// Opens the embedded package logo stream. The caller owns the returned stream.
    /// </summary>
    public StreamInfo? OpenLogoStream()
    {
        var logoItem = Model.LogoItem;
        return logoItem is { IsRef: true } ? Document.Images.TryGetStreamInfo(logoItem.Value) : null;
    }

    /// <summary>
    /// Opens the embedded package logo stream without synchronously waiting for document persistence.
    /// </summary>
    public ValueTask<StreamInfo?> OpenLogoStreamAsync(CancellationToken cancellationToken = default)
    {
        var logoItem = Model.LogoItem;
        return logoItem is { IsRef: true }
            ? Document.Images.TryGetStreamInfoAsync(logoItem.Value, cancellationToken)
            : ValueTask.FromResult<StreamInfo?>(null);
    }

    public ICommand CopyInfo { get; private set; }

    public ICommand PasteInfo { get; private set; }

    /// <summary>
    /// Has quality control.
    /// </summary>
    public bool HasQualityControl
    {
        get => Model.HasQualityControl;
        set
        {
            if (Model.HasQualityControl != value)
            {
                if (value)
                {
                    if (!Document.CheckPackageQuality())
                    {
                        return;
                    }
                }

                Model.HasQualityControl = value;
            }
        }
    }

    public AsyncCommand EnableQualityControl { get; }

    public SimpleCommand DisableQualityControl { get; }

    private bool _isQualityControlUpdatePending;

    /// <summary>
    /// Generates themes with the help of GPT.
    /// </summary>
    public ICommand GenerateThemes { get; private set; }

    public PackageViewModel(Package package, QDocument document)
        : base(package)
    {
        Document = document;

        foreach (var round in package.Rounds)
        {
            Rounds.Add(new RoundViewModel(round) { OwnerPackage = this });
        }

        Tags = new TagsViewModel(this, Model.Tags);

        BindHelper.Bind(Tags, Model.Tags);

        Model.PropertyChanged += Model_PropertyChanged;
        Rounds.CollectionChanged += Rounds_CollectionChanged;

        Add = AddRound = new SimpleCommand(AddRound_Executed);
        AddRestrictions = new SimpleCommand(AddRestrictions_Executed);
        AddTags = new SimpleCommand(AddTags_Executed);

        ChangeLanguage = new SimpleCommand(ChangeLanguage_Executed);
        EnableQualityControl = new AsyncCommand(EnableQualityControl_ExecutedAsync);
        DisableQualityControl = new SimpleCommand(DisableQualityControl_Executed);
        UpdateQualityControlCommands();

        SelectLogo = new AsyncCommand(SelectLogo_ExecutedAsync);
        RemoveLogo = new SimpleCommand(RemoveLogo_Executed);
        UpdateLogoCommands();
        
        CopyInfo = new AsyncCommand(CopyInfo_ExecutedAsync);
        PasteInfo = new AsyncCommand(PasteInfo_ExecutedAsync);
        
        GenerateThemes = new SimpleCommand(GenerateThemes_Executed);
    }

    private async void GenerateThemes_Executed(object? arg)
    {
        try
        {
            await QuestionsGenerator.GenerateThemesAsync(this);
        }
        catch (Exception exc)
        {
            PlatformManager.Instance.ShowErrorMessage(exc.Message);
        }
    }

    private async Task CopyInfo_ExecutedAsync(object? arg)
    {
        try
        {
            var packageInfo = new
            {
                Model.Info.Authors,
                Model.Info.Sources,
                Comments = Model.Info.Comments.Text,
                Model.Tags,
                Model.Restriction,
                Model.Difficulty,
                Model.Name,
                Model.ContactUri,
                Model.Date,
                Model.Publisher
            };
            var legacyJson = JsonSerializer.Serialize(packageInfo);
            await Document.ClipboardService.WriteAsync(new ClipboardWriteRequest
            {
                Text = legacyJson,
                CustomData =
                [
                    new ClipboardCustomData(
                        SIQuesterClipboardSerializer.PackageInfoFormat,
                        SIQuesterClipboardSerializer.SerializePackageInfo(packageInfo)),
                ],
            });
        }
        catch (Exception exc)
        {
            Document.OnError(exc);
        }
    }

    private async Task PasteInfo_ExecutedAsync(object? arg)
    {
        try
        {
            var clipboardData = await Document.ClipboardService.ReadCustomDataAsync(
                SIQuesterClipboardSerializer.PackageInfoFormat);
            Dictionary<string, JsonElement>? info = null;

            if (clipboardData == null
                || !SIQuesterClipboardSerializer.TryDeserializePackageInfo(clipboardData, out info))
            {
                var legacyText = await Document.ClipboardService.ReadTextAsync();

                if (legacyText != null)
                {
                    info = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        Encoding.UTF8.GetBytes(legacyText));
                }
            }

            if (info == null)
            {
                return;
            }

            using var change = Document.OperationsManager.BeginComplexChange();

            foreach (var pair in info)
            {
                switch (pair.Key)
                {
                    case nameof(Model.Info.Authors):
                        Info.Authors.ClearOneByOne();

                        foreach (var item in pair.Value.EnumerateArray())
                        {
                            Info.Authors.Add(item.GetString() ?? "");
                        }
                        break;

                    case nameof(Model.Info.Sources):
                        Info.Sources.ClearOneByOne();

                        foreach (var item in pair.Value.EnumerateArray())
                        {
                            Info.Sources.Add(item.GetString() ?? "");
                        }
                        break;

                    case nameof(Model.Info.Comments):
                        {
                            var value = pair.Value.GetString();

                            if (value != null)
                            {
                                Info.Comments.Text = value;
                            }
                        }
                        break;

                    case nameof(Model.Tags):
                        Tags.ClearOneByOne();

                        foreach (var item in pair.Value.EnumerateArray())
                        {
                            Tags.Add(item.GetString() ?? "");
                        }
                        break;

                    case nameof(Model.Restriction):
                        {
                            var value = pair.Value.GetString();

                            if (value != null)
                            {
                                Model.Restriction = value;
                            }
                        }
                        break;

                    case nameof(Model.Difficulty):
                        {
                            Model.Difficulty = pair.Value.GetInt32();
                        }
                        break;

                    case nameof(Model.Name):
                        {
                            var value = pair.Value.GetString();

                            if (value != null)
                            {
                                Model.Name = value;
                            }
                        }
                        break;

                    case nameof(Model.ContactUri):
                        {
                            var value = pair.Value.GetString();

                            if (value != null)
                            {
                                Model.ContactUri = value;
                            }
                        }
                        break;

                    case nameof(Model.Date):
                        {
                            var value = pair.Value.GetString();

                            if (value != null)
                            {
                                Model.Date = value;
                            }
                        }
                        break;

                    case nameof(Model.Publisher):
                        {
                            var value = pair.Value.GetString();

                            if (value != null)
                            {
                                Model.Publisher = value;
                            }
                        }
                        break;

                    default:
                        break;
                }
            }

            change.Commit();
        }
        catch (Exception exc)
        {
            Document.OnError(exc);
        }
    }

    private void ChangeLanguage_Executed(object? arg)
    {
        Model.Language = Model.Language == "ru-RU" ? "en-US" : "ru-RU";
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Package.Restriction))
        {
            AddRestrictions.CanBeExecuted = Model.Restriction.Length == 0;
        }
        else if (e.PropertyName == nameof(Package.HasQualityControl))
        {
            OnPropertyChanged(nameof(HasQualityControl));
            UpdateQualityControlCommands();
        }
        else if (e.PropertyName == nameof(Package.Logo))
        {
            _logo = null;
            OnPropertyChanged(nameof(Logo));
            OnPropertyChanged(nameof(HasLogo));
            OnPropertyChanged(nameof(LogoName));
            UpdateLogoCommands();
        }
    }

    private async Task EnableQualityControl_ExecutedAsync(object? arg)
    {
        if (Model.HasQualityControl || _isQualityControlUpdatePending)
        {
            return;
        }

        _isQualityControlUpdatePending = true;
        UpdateQualityControlCommands();

        try
        {
            if (await Document.CheckPackageQualityAsync())
            {
                Model.HasQualityControl = true;
            }
        }
        catch (Exception exc)
        {
            Document.OnError(exc);
        }
        finally
        {
            _isQualityControlUpdatePending = false;
            UpdateQualityControlCommands();
        }
    }

    private void DisableQualityControl_Executed(object? arg)
    {
        if (!_isQualityControlUpdatePending)
        {
            Model.HasQualityControl = false;
        }
    }

    private void UpdateQualityControlCommands()
    {
        EnableQualityControl.CanBeExecuted = !Model.HasQualityControl && !_isQualityControlUpdatePending;
        DisableQualityControl.CanBeExecuted = Model.HasQualityControl && !_isQualityControlUpdatePending;
    }

    private void Rounds_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
            case NotifyCollectionChangedAction.Replace:
                if (e.NewItems == null || e.NewStartingIndex < 0)
                {
                    return;
                }

                for (int i = e.NewStartingIndex; i < e.NewStartingIndex + e.NewItems.Count; i++)
                {
                    if (Rounds[i].OwnerPackage != null)
                    {
                        throw new Exception("An attempt to add bound round");
                    }

                    Rounds[i].OwnerPackage = this;
                    Model.Rounds.Insert(i, Rounds[i].Model);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems == null || e.OldStartingIndex < 0)
                {
                    return;
                }

                foreach (RoundViewModel round in e.OldItems)
                {
                    round.OwnerPackage = null;
                    Model.Rounds.RemoveAt(e.OldStartingIndex);

                    Document.ClearLinks(round);
                }
                break;

            case NotifyCollectionChangedAction.Move:
                var movedRound = Model.Rounds[e.OldStartingIndex];
                Model.Rounds.RemoveAt(e.OldStartingIndex);
                Model.Rounds.Insert(e.NewStartingIndex, movedRound);
                break;

            case NotifyCollectionChangedAction.Reset:
                Model.Rounds.Clear();

                foreach (var round in Rounds)
                {
                    round.OwnerPackage = this;
                    Model.Rounds.Add(round.Model);
                }
                break;
        }

        foreach (var round in Rounds)
        {
            round.UpdateStructuralCommands();
        }
    }
    
    private void AddRound_Executed(object? arg)
    {
        var round = new Round
        { 
            Name = (Rounds.Count + 1).ToString(),
            Type = RoundTypes.Standart
        };

        var roundViewModel = new RoundViewModel(round);
        Rounds.Add(roundViewModel);
        QDocument.ActivatedObject = roundViewModel;
        Document.Navigate.Execute(roundViewModel);
    }

    private void AddRestrictions_Executed(object? arg)
    {
        QDocument.ActivatedObject = this;
        Model.Restriction = Resources.Restriction;
    }

    private void AddTags_Executed(object? arg)
    {
        var newTags = PlatformManager.Instance.AskTags(Tags);

        if (newTags == null)
        {
            return;
        }

        using var change = Document.OperationsManager.BeginComplexChange();

        Tags.ClearOneByOne();

        foreach (var item in newTags)
        {
            Tags.Add(item);
        }

        change.Commit();
        OnPropertyChanged(nameof(Tags));
    }

    private async Task SelectLogo_ExecutedAsync(object? arg)
    {
        if (_isLogoUpdatePending)
        {
            return;
        }

        _isLogoUpdatePending = true;
        UpdateLogoCommands();

        try
        {
            if (arg is MediaItemViewModel existingImage)
            {
                Model.Logo = $"@{existingImage.Model.Name}";
                return;
            }

            var imageExtensions = Quality.FileExtensions[CollectionNames.ImagesStorageName]
                .Select(extension => extension.TrimStart('.'))
                .ToArray();
            var pickedFiles = await Document.FilePickerService.PickOpenFilesAsync(new OpenFilePickerRequest(
                Resources.Images,
                [new FileTypeFilter(Resources.Images, imageExtensions)],
                AllowMultiple: false));
            var pickedFile = pickedFiles.FirstOrDefault();

            if (pickedFile == null)
            {
                return;
            }

            using var stagedFile = await Document.Images.StageFileAsync(pickedFile);
            using var change = Document.OperationsManager.BeginComplexChange();
            var image = Document.Images.AddFile(stagedFile);
            Model.Logo = $"@{image.Model.Name}";
            change.Commit();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exc)
        {
            Document.OnError(exc);
        }
        finally
        {
            _isLogoUpdatePending = false;
            UpdateLogoCommands();
        }
    }

    private void RemoveLogo_Executed(object? arg)
    {
        if (!_isLogoUpdatePending)
        {
            Model.Logo = "";
        }
    }

    private void UpdateLogoCommands()
    {
        SelectLogo.CanBeExecuted = !_isLogoUpdatePending;
        RemoveLogo.CanBeExecuted = HasLogo && !_isLogoUpdatePending;
    }

    protected override void UpdateCosts(CostSetter costSetter)
    {
        using var change = Document.OperationsManager.BeginComplexChange();

        base.UpdateCosts(costSetter);

        foreach (var round in Rounds)
        {
            foreach (var theme in round.Themes)
            {
                theme.UpdateCostsCore(costSetter);
            }
        }

        change.Commit();
    }
}
