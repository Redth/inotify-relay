using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InotifyRelay.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInotifyRelayData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlite(connectionString));
        return services;
    }
}
