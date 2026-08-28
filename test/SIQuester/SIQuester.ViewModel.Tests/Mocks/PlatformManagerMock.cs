using SIPackages;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Model;
using SIQuester.ViewModel.PlatformSpecific;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace SIQuester.ViewModel.Tests.Mocks;

/// <summary>
/// Test implementation of PlatformManager that does nothing.
/// </summary>
internal sealed class PlatformManagerMock :
    PlatformManager,
    IFilePickerService,
    IDialogService,
    IApplicationLifetimeService,
    IMediaMaterializationService,
    IPlatformCapabilities,
    IExternalLauncher
{
    public bool SupportsRecoveryManagementUi { get; set; }

    public List<string> RevealedFiles { get; } = [];

    public List<string> Messages { get; } = [];

    public List<string> LegacyExclamationMessages { get; } = [];

    public int PrepareMediaCallCount { get; private set; }

    public override string[] FontFamilies => Array.Empty<string>();

    public override Tuple<int, int, int>? GetCurrentItemSelectionArea() => null;

    public override void CreatePreview(SIDocument document) { }

    public override string[]? ShowOpenUI() => null;

    public override string[]? ShowMediaOpenUI(string mediaCategory, bool allowAnyFile, bool multiselect = true) => null;

    public override bool ShowSaveUI(
        string? title,
        string defaultExtension,
        Dictionary<string, string>? filter,
        [NotNullWhen(true)] ref string? filename) => false;

    public override bool ShowExportUI(
        string title,
        Dictionary<string, string> filter,
        [NotNullWhen(true)] ref string? filename,
        ref int filterIndex,
        out Encoding encoding,
        out bool start)
    {
        encoding = Encoding.UTF8;
        start = false;
        return false;
    }

    public override string? ShowImportUI(string fileExtension, string fileFilter) => null;

    public override string? SelectSearchFolder() => null;

    public override IMedia PrepareMedia(IMedia media, string type)
    {
        PrepareMediaCallCount++;
        return media;
    }

    public override void ClearMedia(IEnumerable<string> media) { }

    public override string? AskText(string title, bool multiline = false) => null;

    public override IEnumerable<string>? AskTags(ItemsViewModel<string> tags) => null;

    public override IFlowDocumentWrapper BuildDocument(SIDocument doc, ExportFormats format) =>
        throw new NotSupportedException();

    public override void ExportTable(SIDocument doc, string filename) { }

    public override void ShowHelp() { }

    public override void AddToRecentCategory(string fileName) { }

    public override void ShowErrorMessage(string message) { }

    public override void ShowExclamationMessage(string message) => LegacyExclamationMessages.Add(message);

    public override void ShowSelectOptionDialog(string message, params UserOption[] options) { }

    public override void Inform(string message, bool exclamation = false) { }

    public override bool Confirm(string message) => true;

    public override bool? ConfirmWithCancel(string message) => true;

    public override bool ConfirmExclamationWithWindow(string message) => true;

    public override void Exit() { }

    public override string CompressImage(string imageUri) => imageUri;

    public override void CopyInfo(object info) { }

    public override Dictionary<string, JsonElement>? PasteInfo() => null;

    public override IDisposable ShowProgressDialog() => new NullDisposable();

    public ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
        OpenFilePickerRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<PickedFile>>(Array.Empty<PickedFile>());

    public ValueTask<PickedFile?> PickSaveFileAsync(
        SaveFilePickerRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<PickedFile?>(null);

    public ValueTask<SaveChangesDecision> ConfirmSaveChangesAsync(
        string message,
        bool allowCancel,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(SaveChangesDecision.Save);

    ValueTask<bool> IDialogService.ConfirmAsync(string message, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    public ValueTask ShowMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Messages.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask ShowErrorAsync(string message, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<string?> SelectOptionAsync(
        string message,
        IReadOnlyList<DialogOption> options,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(options.LastOrDefault()?.Id);

    public void RequestExit() { }

    public void ReleaseMaterializedMedia(IEnumerable<string> mediaNames) { }

    public ValueTask RevealFileAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RevealedFiles.Add(path);
        return ValueTask.CompletedTask;
    }

    private sealed class NullDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
