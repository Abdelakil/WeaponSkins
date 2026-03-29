using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using WeaponSkins.Database;
using WeaponSkins.Services;

namespace WeaponSkins.Injections;

public static class DatabaseServiceInjection
{
    public static IServiceCollection AddStorageService(this IServiceCollection services)
    {
        return services
            .AddSingleton<StorageService>()
            .AddSingleton<EmptyStorageProvider>()
            .AddSingleton<DatabaseService>()
            .AddSingleton<DatabaseSynchronizeService>();
    }

    public static IServiceProvider UseStorageService(this IServiceProvider provider)
    {
        provider.GetRequiredService<StorageService>();
        provider.GetRequiredService<EmptyStorageProvider>();
        provider.GetRequiredService<DatabaseService>();
        provider.GetRequiredService<DatabaseSynchronizeService>();
        return provider;
    }
}