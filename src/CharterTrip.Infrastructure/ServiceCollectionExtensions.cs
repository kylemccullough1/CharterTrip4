using CharterTrip.Core.Abstractions;
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

        services.AddHostedService<TripFlushHostedService>();
        services.AddHostedService<BackupHostedService>();

        // TEMPORARY — see BackupScrubHostedService. Remove once it reports nothing left to find.
        services.AddHostedService<BackupScrubHostedService>();

        return services;
    }
}
