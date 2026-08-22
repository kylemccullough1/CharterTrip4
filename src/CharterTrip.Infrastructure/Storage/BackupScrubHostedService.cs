using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// TEMPORARY. Deletes the treasurer's Venmo handle out of every snapshot already sitting in
/// backups/.
///
/// The handle was removed from the model in v4, so nothing writes it any more, but the rolling
/// backups still hold copies — and on Azure those live inside the App Service where there is no
/// shell to go and edit them. Shipping the scrub as a startup task is the one way to reach them.
///
/// Once the logs show it running with nothing left to find, delete this file and its registration.
/// </summary>
public sealed partial class BackupScrubHostedService(
    IOptions<TripStoreOptions> options,
    ILogger<BackupScrubHostedService> logger) : BackgroundService
{
    private readonly TripStoreOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the v4 migration write its own pre-migration archive first, or we would scrub
        // before the file that most needs scrubbing exists.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        try
        {
            var scrubbed = 0;
            foreach (var path in Files())
                if (ScrubFile(path)) scrubbed++;

            if (scrubbed > 0)
                logger.LogWarning("Removed the Venmo handle from {Count} stored file(s).", scrubbed);
            else
                logger.LogInformation("Backup scrub found nothing to remove — safe to delete BackupScrubHostedService.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup scrub failed.");
        }
    }

    private IEnumerable<string> Files()
    {
        if (File.Exists(_options.TripFilePath)) yield return _options.TripFilePath;

        if (!Directory.Exists(_options.BackupDirectory)) yield break;
        foreach (var path in Directory.EnumerateFiles(_options.BackupDirectory, "*.json"))
            yield return path;
    }

    /// <summary>
    /// Text surgery rather than a JSON round-trip, so every other byte of a backup stays exactly
    /// as it was written. Handles the key whether or not it is the last one in its object.
    /// </summary>
    private bool ScrubFile(string path)
    {
        try
        {
            var original = File.ReadAllText(path);
            if (!original.Contains("\"venmo\"", StringComparison.OrdinalIgnoreCase)) return false;

            var cleaned = VenmoWithComma().Replace(original, "");
            cleaned = TrailingVenmo().Replace(cleaned, "");

            if (cleaned == original) return false;

            AtomicFileWriter.Write(path, cleaned);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not scrub {Path}.", path);
            return false;
        }
    }

    /// <summary>The ordinary case: "venmo": "...", followed by another property.</summary>
    [GeneratedRegex("""[ \t]*"venmo"\s*:\s*"[^"]*"\s*,\s*\r?\n""", RegexOptions.IgnoreCase)]
    private static partial Regex VenmoWithComma();

    /// <summary>The last-property case: the comma belongs to the line before it.</summary>
    [GeneratedRegex(""""",\s*\r?\n[ \t]*"venmo"\s*:\s*"[^"]*""""", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingVenmo();
}
