using CharterTrip.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// Writes any debounced-but-unsaved change to disk as the app shuts down, so an edit made
/// half a second before a deploy or a Ctrl+C isn't lost.
/// </summary>
public sealed class TripFlushHostedService(ITripStore store, ILogger<TripFlushHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.FlushAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Flushed trip data on shutdown.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Shutdown flush failed.");
        }
    }
}
