using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using SIQuester.Avalonia.Views;
using SIQuester.ViewModel.Contracts;
using System.Diagnostics;

namespace SIQuester.Desktop.Services;

internal sealed class DesktopPlatformServices :
    IFilePickerService,
    IDialogService,
    IApplicationLifetimeService,
    IMediaMaterializationService,
    IPlatformService
{
    private const string HelpUri = "https://github.com/VladimirKhil/SI";
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly ILogger<DesktopPlatformServices> _logger;

    public DesktopPlatformServices(
        IClassicDesktopStyleApplicationLifetime lifetime,
        ILogger<DesktopPlatformServices> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public string[] FontFamilies => FontManager.Current.SystemFonts
        .Select(fontFamily => fontFamily.Name)
        .OrderBy(name => name, StringComparer.CurrentCulture)
        .ToArray();

    public void ShowHelp()
    {
        try
        {
            Process.Start(new ProcessStartInfo(HelpUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not open the SI help page");
        }
    }

    public async ValueTask<IReadOnlyList<PickedFile>> PickOpenFilesAsync(
        OpenFilePickerRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = GetOwner();
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = request.AllowMultiple,
            FileTypeFilter = request.FileTypes.Select(ToAvaloniaFileType).ToArray(),
        });

        return files.Select(file => ToPickedFile(file, writable: false)).ToArray();
    }

    public async ValueTask<PickedFile?> PickSaveFileAsync(
        SaveFilePickerRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = await GetOwner().StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedFileName,
            DefaultExtension = request.DefaultExtension,
            FileTypeChoices = request.FileTypes.Select(ToAvaloniaFileType).ToArray(),
        });

        return file == null ? null : ToPickedFile(file, writable: true);
    }

    public async ValueTask<SaveChangesDecision> ConfirmSaveChangesAsync(
        string message,
        bool allowCancel,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new MessageDialogWindow(message, MessageDialogKind.SaveChanges, allowCancel);
        return await dialog.ShowSaveChangesAsync(GetOwner());
    }

    public async ValueTask<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new MessageDialogWindow(message, MessageDialogKind.Confirm);
        return await dialog.ShowConfirmationAsync(GetOwner());
    }

    public async ValueTask ShowMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new MessageDialogWindow(message, MessageDialogKind.Message);
        await dialog.ShowMessageAsync(GetOwner());
    }

    public ValueTask ShowErrorAsync(string message, CancellationToken cancellationToken = default) =>
        ShowMessageAsync(message, cancellationToken);

    public async ValueTask<string?> SelectOptionAsync(
        string message,
        IReadOnlyList<DialogOption> options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new OptionDialogWindow(message, options);
        return await dialog.ShowOptionAsync(GetOwner());
    }

    public void RequestExit() => _lifetime.Shutdown();

    public void ReleaseMaterializedMedia(IEnumerable<string> mediaNames)
    {
        // This host currently previews package streams directly and creates no native copies.
    }

    private Window GetOwner() => _lifetime.MainWindow
        ?? throw new InvalidOperationException("The main window is not available.");

    private static FilePickerFileType ToAvaloniaFileType(FileTypeFilter fileType) => new(fileType.Name)
    {
        Patterns = fileType.Extensions.Select(extension => $"*.{extension.TrimStart('.')}" ).ToArray(),
    };

    private static PickedFile ToPickedFile(IStorageFile file, bool writable) => new(
        file.TryGetLocalPath(),
        file.Name,
        Path.GetExtension(file.Name),
        async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await file.OpenReadAsync();
        },
        writable
            ? async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await file.OpenWriteAsync();
            }
            : null);
}
