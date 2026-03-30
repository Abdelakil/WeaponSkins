using Microsoft.Extensions.DependencyInjection;

namespace WeaponSkins.Injections;

public static class WpCommandServiceInjection
{
    public static IServiceCollection AddWpCommandService(this IServiceCollection services)
    {
        return services
            .AddSingleton<WpCommandService>()
            .AddSingleton<AutoSyncService>();
    }

    public static IServiceProvider UseWpCommandService(this IServiceProvider provider)
    {
        provider.GetRequiredService<WpCommandService>();
        provider.GetRequiredService<AutoSyncService>();
        return provider;
    }
}
