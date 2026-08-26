using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using SIQuester.Avalonia.Views;
using SIQuester.Desktop.Services;
using SIQuester.Model;
using SIQuester.ViewModel;
using SIQuester.ViewModel.Configuration;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIQuester.ViewModel.Helpers;
using SIStatisticsService.Client;
using SIStorage.Service.Client;
using System.Runtime.InteropServices;

namespace SIQuester.Desktop;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            var paths = new DesktopAppPaths();
            paths.EnsureDirectories();
            ConfigureLogging(paths.LogDirectory);

            var settings = AppSettings.Create();
            var currentCulture = Thread.CurrentThread.CurrentUICulture.Name;
            settings.Language = currentCulture == "ru-RU" ? "ru-RU" : "en-US";
            AppSettings.Default = settings;

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SIStatisticsServiceClient:ServiceUri"] = "https://vladimirkhil.com/sistatistics/",
                    ["SIStorageServiceClient:ServiceUri"] = "https://vladimirkhil.com/sistorage/",
                    ["ChgkDbClient:ServiceUri"] = "https://db.chgk.info",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton(settings);
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
            services.AddSingleton<IClipboardService, InMemoryClipboardService>();
            services.AddSingleton<IAppPaths>(paths);

            services.AddSingleton(serviceProvider => new DesktopPlatformServices(
                desktopLifetime,
                serviceProvider.GetRequiredService<ILogger<DesktopPlatformServices>>()));
            services.AddSingleton<IPlatformService>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<IFilePickerService>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<IDialogService>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<IApplicationLifetimeService>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSingleton<IMediaMaterializationService>(serviceProvider => serviceProvider.GetRequiredService<DesktopPlatformServices>());
            services.AddSIQuester();

            _serviceProvider = services.BuildServiceProvider(validateScopes: true);

            _serviceProvider.GetRequiredService<ILogger<App>>().LogInformation(
                "Starting SIQuester Desktop on {OperatingSystem}; architecture {Architecture}; runtime {RuntimeVersion}",
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture,
                RuntimeInformation.FrameworkDescription);

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
                _serviceProvider.GetRequiredService<IApplicationLifetimeService>());

            var mainWindow = new MainWindow { DataContext = mainViewModel };
            desktopLifetime.MainWindow = mainWindow;
            mainWindow.Opened += async (_, _) => await mainViewModel.InitializeAsync();
            desktopLifetime.Exit += (_, _) => _serviceProvider.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

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
