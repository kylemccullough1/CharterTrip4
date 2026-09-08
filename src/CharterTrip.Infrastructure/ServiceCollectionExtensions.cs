using CharterTrip.Core;
using CharterTrip.Core.Abstractions;
using CharterTrip.Infrastructure.Photos;
using CharterTrip.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // The real stores are always built, in both modes, and are always the only things that
        // touch the disk. In showcase mode they stop being what the app injects and become the
        // baseline the sandboxes copy — which is what makes the showcase read as the finished
        // trip rather than an empty one.
        services.AddSingleton<JsonTripStore>();
        services.AddSingleton<FileSystemPhotoStore>();

        if (SiteMode.ReadOnly)
        {
            // SCOPED, which on Blazor Server means one per browser tab's circuit. That single
            // word is what makes edits real and temporary at the same time: they land on this
            // visitor's copy, and the copy dies with the connection. See SandboxTripStore.
            services.AddScoped<ITripStore, SandboxTripStore>();

            // Singleton, because an uploaded picture is fetched back over a separate HTTP request
            // that is not part of the circuit and could not see a scoped cache. See
            // SandboxPhotoStore, which explains why sharing it is safe.
            services.AddSingleton<IPhotoStore>(sp => new SandboxPhotoStore(
                sp.GetRequiredService<FileSystemPhotoStore>(),
                sp.GetRequiredService<ILogger<SandboxPhotoStore>>()));
        }
        else
        {
            services.AddSingleton<ITripStore>(sp => sp.GetRequiredService<JsonTripStore>());
            services.AddSingleton<IPhotoStore>(sp => sp.GetRequiredService<FileSystemPhotoStore>());
        }

        // Both take JsonTripStore rather than ITripStore. They are the file's lifecycle — a
        // shutdown flush and a rolling backup of trip.json — so the concrete store is the honest
        // dependency, and it is also the only one that still exists as a singleton in showcase
        // mode. A hosted service cannot hold a scoped sandbox, and would have nothing to do with
        // one if it could.
        services.AddHostedService<TripFlushHostedService>();
        services.AddHostedService<BackupHostedService>();

        return services;
    }
}
