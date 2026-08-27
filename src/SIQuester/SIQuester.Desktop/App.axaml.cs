using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using SIQuester.Avalonia.Localization;
using SIQuester.Avalonia.Services;
using SIQuester.Avalonia.Views;
using SIQuester.Desktop.Services;
using SIQuester.Model;
using SIQuester.ViewModel;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Helpers;
using SIQuester.ViewModel.Services;
using SIStatisticsService.Client;
using SIStorage.Service.Client;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SIQuester.Desktop;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private Task? _initializationTask;
    private AppSettings? _settings;
    private ISettingsStore? _settingsStore;
    private bool _settingsNeedSave;
    private bool _settingsReadOnly;
    private MainViewModel? _mainViewModel;
    private DispatcherTimer? _autoSaveTimer;
    private readonly CancellationTokenSource _applicationCancellation = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        base.OnFrameworkInitializationCompleted();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime initializedLifetime)
        {
            _initializationTask = InitializeDesktopAsync(initializedLifetime);
        }
    }

    private async Task InitializeDesktopAsync(IClassicDesktopStyleApplicationLifetime desktopLifetime)
    {
        try
        {
            var paths = new PlatformAppPaths();
            paths.EnsureDirectories();
            ConfigureLogging(paths.LogDirectory);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SIStatisticsServiceClient:ServiceUri"] = "https://vladimirkhil.com/sistatistics/",
                    ["SIStorageServiceClient:ServiceUri"] = "https://vladimirkhil.com/sistorage/",
                    ["ChgkDbClient:ServiceUri"] = "https://db.chgk.info",
                })
                .Build();

            AppSettings? loadedSettings = null;
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton(_ => loadedSettings
                ?? throw new InvalidOperationException("Application settings have not finished loading."));
            services.Configure<AppOptions>(configuration.GetSection(AppOptions.ConfigurationSectionName));
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
                builder.AddNLog();
            });
            services.AddSIStatisticsServiceClient(configuration);
            services.AddSIStorageServiceClient(configuration);
            services.AddChgkServiceClient(configuration);
            services.AddSingleton<IClipboardService>(_ =>
                new AvaloniaClipboardService(() => desktopLifetime.MainWindow));
            services.AddSingleton<IAppPaths>(paths);
            services.AddSingleton<ISettingsStore, JsonSettingsStore>();
            services.AddSingleton(serviceProvider => new DesktopPlatformServices(
                desktopLifetime,
                serviceProvider.GetRequiredService<ILogger<DesktopPlatformServices>>()));
            services.AddSingleton<IPlatformService>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<IFilePickerService>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<IDialogService>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<IApplicationLifetimeService>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<IMediaMaterializationService>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<IPlatformCapabilities>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<DesktopQuestionPreviewService>();
            services.AddSingleton<IQuestionPreviewService>(serviceProvider =>
                serviceProvider.GetRequiredService<DesktopQuestionPreviewService>());
            services.AddSingleton<IMediaPreviewService>(serviceProvider =>
                serviceProvider.GetRequiredService<DesktopQuestionPreviewService>());
            services.AddSingleton<IExternalLauncher>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<IUiDispatcher>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSIQuester();

            _serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            _settingsStore = _serviceProvider.GetRequiredService<ISettingsStore>();
            var loadResult = await _settingsStore.LoadAsync();
            loadedSettings = _settings = loadResult.Settings;
            _settingsNeedSave = loadResult.NeedsSave;
            _settingsReadOnly = loadResult.IsReadOnly;

            var language = _settings.Language is "ru-RU" or "en-US"
                ? _settings.Language
                : CultureInfo.CurrentUICulture.Name == "ru-RU" ? "ru-RU" : "en-US";
            _settings.Language = language;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            AppSettings.Default = _settings;
            ApplyTheme(_settings.DesktopTheme);
            _settings.PropertyChanged += Settings_PropertyChanged;

            logger.LogInformation(
                "Starting SIQuester Cross-Platform on {OperatingSystem}; architecture {Architecture}; runtime {RuntimeVersion}; settings read-only: {SettingsReadOnly}",
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture,
                RuntimeInformation.FrameworkDescription,
                _settingsReadOnly);
            logger.LogInformation("Question preview native-backend probing is deferred until preview is requested");

            var mainViewModel = new MainViewModel(
                desktopLifetime.Args ?? Array.Empty<string>(),
                _serviceProvider.GetRequiredService<IOptions<AppOptions>>().Value,
                _serviceProvider.GetRequiredService<IClipboardService>(),
                _serviceProvider,
                _serviceProvider.GetRequiredService<IPlatformService>(),
                _serviceProvider.GetRequiredService<IDocumentViewModelFactory>(),
                _serviceProvider.GetRequiredService<ILoggerFactory>(),
                _serviceProvider.GetRequiredService<IFilePickerService>(),
                _serviceProvider.GetRequiredService<IDialogService>(),
                _serviceProvider.GetRequiredService<IUiDispatcher>(),
                _serviceProvider.GetRequiredService<IApplicationLifetimeService>(),
                _serviceProvider.GetRequiredService<IDocumentRecoveryService>(),
                _serviceProvider.GetRequiredService<IPlatformCapabilities>(),
                _serviceProvider.GetRequiredService<IExternalLauncher>());
            _mainViewModel = mainViewModel;

            var mainWindow = new MainWindow(PersistSettingsBeforeCloseAsync) { DataContext = mainViewModel };
            desktopLifetime.MainWindow = mainWindow;
            desktopLifetime.ShutdownMode = ShutdownMode.OnLastWindowClose;
            mainWindow.Opened += async (_, _) => await InitializeMainViewModelAsync(mainViewModel, logger);
            desktopLifetime.Exit += (_, _) =>
            {
                _autoSaveTimer?.Stop();
                if (_autoSaveTimer != null)
                {
                    _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
                }
                _applicationCancellation.Cancel();
                _settings.PropertyChanged -= Settings_PropertyChanged;
                mainViewModel.Dispose();
                _serviceProvider.Dispose();
                _applicationCancellation.Dispose();
            };
            mainWindow.Show();

            if (_settings.AutoSave)
            {
                _autoSaveTimer = new DispatcherTimer { Interval = AppSettings.AutoSaveInterval };
                _autoSaveTimer.Tick += AutoSaveTimer_Tick;
                _autoSaveTimer.Start();
            }
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Fatal(exception, "SIQuester desktop initialization failed");
            desktopLifetime.Shutdown(1);
        }
    }

    private async void AutoSaveTimer_Tick(object? sender, EventArgs eventArgs)
    {
        if (_mainViewModel == null || _applicationCancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await _mainViewModel.AutoSaveAsync(_applicationCancellation.Token);
        }
        catch (OperationCanceledException) when (_applicationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _serviceProvider?.GetRequiredService<ILogger<App>>()
                .LogError(exception, "Automatic recovery snapshot failed");
        }
    }

    private static async Task InitializeMainViewModelAsync(
        MainViewModel mainViewModel,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            await mainViewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Main workspace initialization failed");
        }
    }

    private async ValueTask PersistSettingsBeforeCloseAsync(CancellationToken cancellationToken)
    {
        if (_settings == null || _settingsStore == null || _settingsReadOnly
            || (!_settingsNeedSave && !_settings.HasChanges))
        {
            return;
        }

        try
        {
            await _settingsStore.SaveAsync(_settings, cancellationToken);
            _settingsNeedSave = false;
        }
        catch (Exception exception)
        {
            _serviceProvider?.GetRequiredService<ILogger<App>>()
                .LogError(exception, "Application settings could not be saved");

            if (_serviceProvider != null)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .ShowErrorAsync(UiStrings.SettingsSavingError, cancellationToken);
            }
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(AppSettings.DesktopTheme))
        {
            ApplyTheme(_settings?.DesktopTheme ?? DesktopThemePreference.System);
        }
    }

    private void ApplyTheme(DesktopThemePreference preference) => RequestedThemeVariant = preference switch
    {
        DesktopThemePreference.Light => ThemeVariant.Light,
        DesktopThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private static void ConfigureLogging(string logDirectory)
    {
        var configuration = new LoggingConfiguration();
        var fileTarget = new FileTarget("file")
        {
            FileName = Path.Combine(logDirectory, "siquester.log"),
            Layout = "${longdate}|${uppercase:${level}}|${logger}|${message} ${exception:format=tostring}",
            ArchiveAboveSize = 5 * 1024 * 1024,
            MaxArchiveFiles = 3,
        };

        configuration.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, fileTarget);
        LogManager.Configuration = configuration;
    }
}
