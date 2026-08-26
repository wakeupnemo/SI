using Avalonia.Headless.NUnit;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using SIPackages;
using SIQuester.Avalonia.Localization;
using SIQuester.Avalonia.Services;
using SIQuester.Avalonia.Views;
using SIQuester.ViewModel;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Model;
using SIStatisticsService.Contract;
using SIStorage.Service.Contract;
using System.Globalization;

namespace SIQuester.Avalonia.Tests;

[TestFixture]
internal sealed class ViewSmokeTests
{
    [AvaloniaTest]
    public void MainWindow_InstantiatesWithCompiledBindings()
    {
        var window = new MainWindow();

        Assert.That(window.Content, Is.Not.Null);
    }

    [AvaloniaTest]
    public void Inspector_AcceptsTypedQuestionSelection()
    {
        var question = new QuestionViewModel(new Question { Price = 300 });
        var inspector = new InspectorView { SelectedItem = question };

        Assert.That(inspector.SelectedItem, Is.SameAs(question));
    }

    [AvaloniaTest]
    public void DocumentEditor_InitialSelectionFlowsToTypedInspector()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Selection test", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Selection test");
        var view = new DocumentEditorView { DataContext = documentViewModel };
        var window = new Window { Content = view };

        window.Show();
        view.UpdateLayout();

        var inspector = view.FindControl<InspectorView>("Inspector");
        Assert.Multiple(() =>
        {
            Assert.That(documentViewModel.ActiveNode, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector, Is.Not.Null);
            Assert.That(inspector!.SelectedItem, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector.GetVisualDescendants().OfType<TextBox>(), Is.Not.Empty);
        });

        window.Close();
        documentViewModel.Dispose();
    }

    [AvaloniaTest]
    public void DocumentEditor_DocumentAttachedAfterViewIsShown_PreservesInitialSelection()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Delayed selection test", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Delayed selection test");
        var view = new DocumentEditorView();
        var window = new Window { Content = view };

        window.Show();
        view.DataContext = documentViewModel;
        view.UpdateLayout();

        var inspector = view.FindControl<InspectorView>("Inspector");
        Assert.Multiple(() =>
        {
            Assert.That(documentViewModel.ActiveNode, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector!.SelectedItem, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector.GetVisualDescendants().OfType<TextBox>(), Is.Not.Empty);
        });

        window.Close();
        documentViewModel.Dispose();
    }

    [AvaloniaTest]
    public void MainWindow_DocumentOpenedAfterStartup_PreservesInitialSelection()
    {
        using var serviceProvider = CreateServiceProvider();
        using var document = SIDocument.Create("Startup selection test", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Startup selection test");
        var mainViewModel = CreateMainViewModel(serviceProvider);
        var window = new MainWindow { DataContext = mainViewModel };

        window.Show();
        mainViewModel.DocList.Add(documentViewModel);
        window.UpdateLayout();

        var inspector = window.GetVisualDescendants().OfType<InspectorView>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(mainViewModel.ActiveDocument, Is.SameAs(documentViewModel));
            Assert.That(documentViewModel.ActiveNode, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector.SelectedItem, Is.SameAs(documentViewModel.Package));
            Assert.That(inspector.GetVisualDescendants().OfType<TextBox>(), Is.Not.Empty);
        });

        window.DataContext = null;
        window.Close();
        documentViewModel.Dispose();
    }

    [AvaloniaTest]
    public void MainWindow_CloseWithNoDocuments_CompletesWithoutReentrantPrompt()
    {
        using var serviceProvider = CreateServiceProvider();
        var window = new MainWindow { DataContext = CreateMainViewModel(serviceProvider) };

        window.Show();
        window.Close();

        Assert.That(window.IsVisible, Is.False);
    }

    [AvaloniaTest]
    public void MainWindow_CloseWithNoDocuments_RunsSettingsPersistenceOnce()
    {
        using var serviceProvider = CreateServiceProvider();
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var persistenceCalls = 0;
        var window = new MainWindow(_ =>
        {
            persistenceCalls++;
            return ValueTask.CompletedTask;
        })
        {
            DataContext = mainViewModel,
        };

        window.Show();
        window.Close();

        Assert.Multiple(() =>
        {
            Assert.That(window.IsVisible, Is.False);
            Assert.That(persistenceCalls, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void EmptyState_RendersRecentFilesWithOpenCommand()
    {
        using var serviceProvider = CreateServiceProvider();
        var recentPath = Path.Combine(Path.GetTempPath(), "Папка с пробелами", "пакет 例.siq");
        AppSettings.Default.History.Add(recentPath);
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var window = new MainWindow { DataContext = mainViewModel };

        try
        {
            window.Show();
            window.UpdateLayout();
            var recentButton = window.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.CommandParameter, recentPath));

            Assert.Multiple(() =>
            {
                Assert.That(recentButton.Command, Is.SameAs(mainViewModel.OpenRecent));
                Assert.That(ToolTip.GetTip(recentButton), Is.EqualTo(recentPath));
            });
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public void SettingsView_UpdatesThemeAndLanguageThroughCompiledBindings()
    {
        AppSettings.Default = AppSettings.Create();
        var viewModel = new SettingsViewModel(Substitute.For<IPlatformService>());
        var view = new SettingsView { DataContext = viewModel };
        var window = new Window { Content = view };

        window.Show();
        view.UpdateLayout();

        var selectors = view.GetVisualDescendants().OfType<ComboBox>().ToArray();
        Assert.That(selectors, Has.Length.EqualTo(2));

        selectors[0].SelectedItem = DesktopThemePreference.Dark;
        selectors[1].SelectedItem = "ru-RU";

        Assert.Multiple(() =>
        {
            Assert.That(AppSettings.Default.DesktopTheme, Is.EqualTo(DesktopThemePreference.Dark));
            Assert.That(AppSettings.Default.Language, Is.EqualTo("ru-RU"));
        });

        window.Close();
    }

    [AvaloniaTest]
    public async Task AvaloniaClipboardService_RoundTripsTypedFormats()
    {
        var window = new Window();
        window.Show();
        var service = new AvaloniaClipboardService(() => window);
        var customFormat = new ClipboardCustomFormat(
            "SIQuester.Tests.v1",
            ClipboardCustomDataKind.Binary);
        var imagePng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester clipboard тест {Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(filePath, "clipboard file");
            await service.WriteAsync(new ClipboardWriteRequest
            {
                Text = "Буфер обмена",
                FilePaths = [filePath],
                ImagePng = imagePng,
                CustomData = [new ClipboardCustomData(customFormat, new byte[] { 7, 8, 9 })],
            });

            var text = await service.ReadTextAsync();
            var filePaths = await service.ReadFilePathsAsync();
            var roundTrippedImagePng = await service.ReadImagePngAsync();
            var customData = await service.ReadCustomDataAsync(customFormat);

            Assert.Multiple(() =>
            {
                Assert.That(text, Is.EqualTo("Буфер обмена"));
                Assert.That(filePaths, Does.Contain(filePath));
                Assert.That(roundTrippedImagePng, Is.EqualTo(imagePng));
                Assert.That(customData, Is.EqualTo(new byte[] { 7, 8, 9 }));
            });
        }
        finally
        {
            await service.ClearAsync();
            window.Close();
            File.Delete(filePath);
        }
    }

    [AvaloniaTest]
    public async Task CtrlC_WithDocumentFocus_InvokesDocumentCopy()
    {
        var clipboardService = Substitute.For<IClipboardService>();
        clipboardService
            .WriteAsync(Arg.Any<ClipboardWriteRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        using var serviceProvider = CreateServiceProvider(clipboardService);
        using var document = SIDocument.Create("Shortcut test", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Shortcut test");
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var window = new MainWindow { DataContext = mainViewModel };

        try
        {
            window.Show();
            mainViewModel.DocList.Add(documentViewModel);
            window.UpdateLayout();

            var tree = window.GetVisualDescendants().OfType<TreeView>().Single();
            tree.Focus();
            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            await Dispatcher.UIThread.InvokeAsync(() => { });

            await clipboardService.Received(1).WriteAsync(
                Arg.Is<ClipboardWriteRequest>(request => request.CustomData.Count > 0),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task CtrlC_WithFocusedTextBox_DoesNotInvokeDocumentCopy()
    {
        var clipboardService = Substitute.For<IClipboardService>();
        clipboardService
            .WriteAsync(Arg.Any<ClipboardWriteRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        using var serviceProvider = CreateServiceProvider(clipboardService);
        using var document = SIDocument.Create("Text shortcut test", "Test author");
        var documentViewModel = serviceProvider
            .GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(document, "Text shortcut test");
        using var mainViewModel = CreateMainViewModel(serviceProvider);
        var window = new MainWindow { DataContext = mainViewModel };

        try
        {
            window.Show();
            mainViewModel.DocList.Add(documentViewModel);
            window.UpdateLayout();

            var textBox = window.GetVisualDescendants().OfType<TextBox>().First();
            textBox.Text = "Selected text";
            textBox.SelectionStart = 0;
            textBox.SelectionEnd = textBox.Text.Length;
            textBox.Focus();
            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            await Dispatcher.UIThread.InvokeAsync(() => { });

            await clipboardService.DidNotReceive().WriteAsync(
                Arg.Any<ClipboardWriteRequest>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            documentViewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public void CoreViews_InstantiateWithoutNativeServices()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new NewPackageView(), Is.Not.Null);
            Assert.That(new DocumentEditorView(), Is.Not.Null);
            Assert.That(new SettingsView(), Is.Not.Null);
            Assert.That(new MessageDialogWindow(), Is.Not.Null);
            Assert.That(
                new OptionDialogWindow("Recovery", [new DialogOption("close", "Close")]),
                Is.Not.Null);
        });
    }

    [Test]
    public void RussianResources_AreAvailable()
    {
        var previousCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.That(UiStrings.New, Is.EqualTo("Создать"));
            Assert.That(UiStrings.Save, Is.EqualTo("Сохранить"));
            Assert.That(UiStrings.Options, Is.EqualTo("Настройки"));
            Assert.That(
                new DesktopThemeLabelConverter().Convert(
                    DesktopThemePreference.System,
                    typeof(string),
                    null,
                    CultureInfo.CurrentUICulture),
                Is.EqualTo("Системная тема"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static ServiceProvider CreateServiceProvider(IClipboardService? clipboardService = null)
    {
        AppSettings.Default = new AppSettings();
        var services = new ServiceCollection();
        services.AddSIQuester();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        var appPaths = Substitute.For<IAppPaths>();
        appPaths.RecoveryDirectory.Returns(Path.Combine(Path.GetTempPath(), "SIQuester.Avalonia.Tests", Guid.NewGuid().ToString("N")));
        services.AddSingleton(appPaths);
        services.AddSingleton(clipboardService ?? Substitute.For<IClipboardService>());
        services.AddSingleton(Substitute.For<IFilePickerService>());
        services.AddSingleton(Substitute.For<IDialogService>());
        services.AddSingleton(Substitute.For<IApplicationLifetimeService>());
        services.AddSingleton(Substitute.For<IMediaMaterializationService>());
        services.AddSingleton(Substitute.For<IPlatformService>());
        services.AddSingleton(Substitute.For<ISIStatisticsServiceClient>());
        services.AddSingleton(Substitute.For<ISIStorageServiceClient>());

        var templatesRepository = Substitute.For<IPackageTemplatesRepository>();
        templatesRepository.Templates.Returns(new List<PackageTemplate>());
        services.AddSingleton(templatesRepository);
        services.AddSingleton(serviceProvider => new StorageContextViewModel(
            serviceProvider.GetRequiredService<ISIStorageServiceClient>(),
            AppSettings.Default,
            serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<StorageContextViewModel>()));

        return services.BuildServiceProvider();
    }

    private static MainViewModel CreateMainViewModel(IServiceProvider serviceProvider) => new(
        Array.Empty<string>(),
        new AppOptions(),
        serviceProvider.GetRequiredService<IClipboardService>(),
        serviceProvider,
        serviceProvider.GetRequiredService<IPlatformService>(),
        serviceProvider.GetRequiredService<IDocumentViewModelFactory>(),
        serviceProvider.GetRequiredService<ILoggerFactory>(),
        serviceProvider.GetRequiredService<IFilePickerService>(),
        serviceProvider.GetRequiredService<IDialogService>(),
        serviceProvider.GetRequiredService<IApplicationLifetimeService>(),
        serviceProvider.GetRequiredService<IDocumentRecoveryService>());
}
