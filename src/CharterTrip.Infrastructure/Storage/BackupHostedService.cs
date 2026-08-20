using CharterTrip.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// Copies trip.json aside on startup and on a timer, keeping the most recent N.
/// This exists because the realistic failure on the night isn't a disk fault, it's a person
/// deleting the wrong thing at 1am. Restoring is then "copy a file back".
/// </summary>
public sealed class BackupHostedService(
    ITripStore store,
    IOptions<TripStoreOptions> options,
    ILogger<BackupHostedService> logger) : BackgroundService
{
    private readonly TripStoreOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TakeBackupAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _options.BackupIntervalMinutes)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await TakeBackupAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task TakeBackupAsync(CancellationToken ct)
    {
        try
        {
            // Make sure what's on disk is current before copying it.
            await store.FlushAsync(ct).ConfigureAwait(false);

            var source = _options.TripFilePath;
            if (!File.Exists(source)) return;

            Directory.CreateDirectory(_options.BackupDirectory);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            File.Copy(source, Path.Combine(_options.BackupDirectory, $"trip-{stamp}.json"), overwrite: true);

            Prune();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup failed.");
        }
    }

    private void Prune()
    {
        var keep = Math.Max(1, _options.BackupsToKeep);
        var stale = new DirectoryInfo(_options.BackupDirectory)
            .GetFiles("trip-*.json")
            .OrderByDescending(f => f.Name)
            .Skip(keep);

        foreach (var file in stale)
        {
            try { file.Delete(); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not prune {File}.", file.Name); }
        }
    }
}
