using System.Runtime.InteropServices;
using InotifyRelay.Core.Watching;
using InotifyRelay.Watcher.Linux;
using InotifyRelay.Watcher.Managed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InotifyRelay.Watcher;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInotifyRelayWatcher(this IServiceCollection services)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            services.TryAddSingleton<IFileSystemWatcher, LinuxInotifyWatcher>();
        else
            services.TryAddSingleton<IFileSystemWatcher, ManagedFsWatcher>();
        return services;
    }
}
