namespace CharterTrip.Infrastructure.Storage;

public sealed class TripStoreOptions
{
    public const string SectionName = "Trip";

    /// <summary>
    /// Where trip.json, backups/ and photos/ live.
    /// Locally this is ./data. On Azure App Service it MUST be under /home
    /// (set Trip__DataRoot=/home/data) because everything outside /home is wiped on deploy.
    /// </summary>
    public string DataRoot { get; set; } = "data";

    public string FileName { get; set; } = "trip.json";

    /// <summary>Wait this long after the last edit before writing, so a burst of keystrokes costs one write.</summary>
    public int DebounceMilliseconds { get; set; } = 500;

    public int BackupIntervalMinutes { get; set; } = 15;
    public int BackupsToKeep { get; set; } = 20;

    public string TripFilePath => Path.Combine(DataRoot, FileName);
    public string BackupDirectory => Path.Combine(DataRoot, "backups");
    public string PhotoDirectory => Path.Combine(DataRoot, "photos");
}
