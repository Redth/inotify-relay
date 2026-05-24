using InotifyRelay.Core.Providers;
using InotifyRelay.Core.Templating;
using InotifyRelay.Providers.Jellyfin;
using InotifyRelay.Providers.Plex;
using InotifyRelay.Providers.Webhook;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InotifyRelay.Providers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInotifyRelayProviders(this IServiceCollection services)
    {
        services.TryAddSingleton<ITemplateFilterRegistry>(_ => TemplateFilterRegistry.CreateDefault());
        services.AddHttpClient("relay").ConfigureHttpClient(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("inotify-relay/0.1");
        });

        services.AddSingleton<IRelayProvider, WebhookProvider>();
        services.AddSingleton<IRelayProvider, JellyfinProvider>();
        services.AddSingleton<IRelayProvider, PlexProvider>();
        return services;
    }
}
