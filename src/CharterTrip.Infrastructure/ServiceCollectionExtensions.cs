using CharterTrip.Core.Abstractions;
using CharterTrip.Infrastructure.Mystery;
using CharterTrip.Infrastructure.Photos;
using CharterTrip.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CharterTrip.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Everything Infrastructure provides, in one call. Program.cs says
    /// builder.Services.AddTripStorage(builder.Configuration) and doesn't need to know
    /// that any of this is backed by files.
    /// </summary>
    public static IServiceCollection AddTripStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TripStoreOptions>(configuration.GetSection(TripStoreOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<JsonTripStore>();
        services.AddSingleton<ITripStore>(sp => sp.GetRequiredService<JsonTripStore>());
        services.AddSingleton<IPhotoStore, FileSystemPhotoStore>();

        // The murder mystery's authored content: read once, never written, so a singleton of the
        // immutable record rather than a service. ScriptLoader.Load() validates and throws, which
        // is why this is resolved during startup in Program.cs rather than on first page hit.
        services.AddSingleton(_ => ScriptLoader.Load());

        services.AddHostedService<TripFlushHostedService>();
        services.AddHostedService<BackupHostedService>();


        return services;
    }
}
