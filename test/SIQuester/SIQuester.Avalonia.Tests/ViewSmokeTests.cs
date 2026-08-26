using Avalonia.Headless.NUnit;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using SIPackages;
using SIQuester.Avalonia.Localization;
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
    public void CoreViews_InstantiateWithoutNativeServices()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new NewPackageView(), Is.Not.Null);
            Assert.That(new DocumentEditorView(), Is.Not.Null);
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
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static ServiceProvider CreateServiceProvider()
    {
        AppSettings.Default = new AppSettings();
        var services = new ServiceCollection();
        services.AddSIQuester();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(Substitute.For<IClipboardService>());
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
        serviceProvider.GetRequiredService<IApplicationLifetimeService>());
}
