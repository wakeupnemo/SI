using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Services;
using SIStorageService.ViewModel;

namespace SIQuester.ViewModel;

/// <summary>
/// Allows to register SIQuester view model in <see cref="IServiceCollection" />.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers SIQuester view model in <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">Services collection.</param>
    public static IServiceCollection AddSIQuester(this IServiceCollection services)
    {
        services.AddSingleton<IPackageTemplatesRepository, PackageTemplatesRepository>();
        services.AddSingleton<StorageViewModel>();
        services.AddSingleton<StorageContextViewModel>();
        services.TryAddSingleton<IAppPaths, PlatformAppPaths>();
        services.TryAddSingleton<IUiDispatcher, InlineUiDispatcher>();
        services.AddSingleton<IDocumentPersistenceService, SafeDocumentPersistenceService>();
        services.AddSingleton<IDocumentRecoveryService, DocumentRecoveryService>();
        services.AddSingleton<IDocumentViewModelFactory, DocumentViewModelFactory>();

        return services;
    }
}
