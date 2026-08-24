using System.Text.Json;
using System.Text.Json.Nodes;
using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
using CharterTrip.Infrastructure.Seed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// The whole trip lives in memory in one object; the JSON file is just where it goes to
/// survive a restart.
///
/// Why not a database? The dataset is a few hundred KB, one process owns it, and being able
/// to read the state in a git diff is genuinely useful. If that stops being true, write a
/// SqlTripStore next to this one — nothing outside Infrastructure knows the difference.
///
/// Registered as a SINGLETON. One instance, one file, one writer.
/// </summary>
public sealed class JsonTripStore : ITripStore, IAsyncDisposable
{
    private readonly TripStoreOptions _options;
    private readonly ILogger<JsonTripStore> _logger;
    private readonly IClock _clock;

    /// <summary>Guards the in-memory object.</summary>
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    /// <summary>Guards the file, so two flushes can never interleave.</summary>
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    private readonly TripData _current;
    private CancellationTokenSource? _debounce;
    private bool _disposed;

    /// <summary>Set once by LoadOrSeed; nothing afterwards changes where the data came from.</summary>
    private bool _seeded;

    /// <summary>Timestamp of the last write we made, so we can spot writes we did not make.</summary>
    private DateTime? _lastWriteWeMade;

    public JsonTripStore(IOptions<TripStoreOptions> options, ILogger<JsonTripStore> logger, IClock clock)
    {
        _options = options.Value;
        _logger = logger;
        _clock = clock;
        _current = LoadOrSeed();
    }

    public TripData Current => _current;

    public TripStoreStatus Status => new(
        Path.GetFullPath(_options.TripFilePath),
        _seeded,
        DataDirectoryIsWritable());

    public event Func<TripChanged, Task>? Changed;

    public async Task MutateAsync(Action<TripData> mutate, TripArea area, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ObjectDisposedException.ThrowIf(_disposed, this);

        TripChanged change;

        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            mutate(_current);
            _current.Revision++;
            _current.UpdatedUtc = _clock.UtcNow;
            change = new TripChanged(area, _current.Revision);
        }
        finally
        {
            _stateGate.Release();
        }

        ScheduleFlush();
        await RaiseChangedAsync(change).ConfigureAwait(false);
    }

    public async Task ReplaceAsync(TripData replacement, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Copy aside what is about to be discarded, before a single field is touched. An import
        // is the one operation here with no undo, and the person doing it is usually doing it in
        // a hurry.
        ArchiveBeforeReplace();

        TripChanged change;

        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previous = _current.Revision;
            TripReplace.Overwrite(_current, replacement);

            // The revision belongs to this store, not to the uploaded file. Taking the file's
            // number would let the revision go backwards, which is exactly the thing it exists
            // to make impossible to misread in a log.
            _current.Revision = previous + 1;
            _current.UpdatedUtc = _clock.UtcNow;

            change = new TripChanged(TripArea.All, _current.Revision);
        }
        finally
        {
            _stateGate.Release();
        }

        // Not ScheduleFlush: nobody should have to wonder whether an import survived a crash in
        // the next four hundred milliseconds.
        await FlushAsync(ct).ConfigureAwait(false);

        _logger.LogWarning(
            "Trip data was replaced wholesale (now revision {Revision}, {People} people, {Items} itinerary items).",
            _current.Revision, _current.Roster.Count, _current.Itinerary.Sum(d => d.Items.Count));

        await RaiseChangedAsync(change).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        CancelPendingFlush();
        await WriteToDiskAsync(ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- loading

    private TripData LoadOrSeed()
    {
        var path = _options.TripFilePath;

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);

                // Some historical shapes cannot be deserialized at all. Repair those first —
                // otherwise one section the reader chokes on quarantines the whole trip.
                var node = JsonNode.Parse(json);
                if (LegacyJsonShapes.Normalize(node))
                {
                    json = node!.ToJsonString(TripJson.Options);
                    _logger.LogInformation(
                        "Rewrote a pre-v9 section of {Path} so the rest of the file could be read.", path);
                }

                var loaded = JsonSerializer.Deserialize<TripData>(json, TripJson.Options);
                if (loaded is not null)
                {
                    var from = loaded.SchemaVersion;
                    if (TripMigrations.Apply(loaded))
                    {
                        _logger.LogInformation(
                            "Migrated trip data from schema v{From} to v{To}.", from, loaded.SchemaVersion);

                        // Keep a copy of the pre-migration file. If a migration is ever wrong,
                        // this is the thing that saves the weekend.
                        TryArchivePreMigration(path, json, from);
                        AtomicFileWriter.Write(path, JsonSerializer.Serialize(loaded, TripJson.Options));
                    }

                    RememberOurWrite();
                    {
                    }

                    _logger.LogInformation("Loaded trip data from {Path} (revision {Revision}).", path, loaded.Revision);
                    return loaded;
                }

                _logger.LogError("{Path} deserialized to null. Falling back to the seed.", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not read {Path}. Falling back to the seed.", path);
            }

            QuarantineUnreadableFile(path);
        }

        var seeded = SeedLoader.Load();
        TripMigrations.Apply(seeded);
        seeded.UpdatedUtc = _clock.UtcNow;
        _seeded = true;

        try
        {
            AtomicFileWriter.Write(path, JsonSerializer.Serialize(seeded, TripJson.Options));
            RememberOurWrite();
            _logger.LogInformation("No trip data found — wrote a fresh seed to {Path}.", path);
        }
        catch (Exception ex)
        {
            // Running with a read-only data directory is survivable: we just can't persist.
            _logger.LogError(ex, "Seeded in memory but could not write {Path}.", path);
        }

        return seeded;
    }

    /// <summary>Snapshot the file as it was before a schema migration touched it.</summary>
    private void TryArchivePreMigration(string path, string originalJson, int fromVersion)
    {
        try
        {
            var dir = Path.Combine(_options.BackupDirectory);
            Directory.CreateDirectory(dir);
            var name = $"trip-pre-v{fromVersion}-{_clock.UtcNow:yyyyMMdd-HHmmss}.json";
            AtomicFileWriter.Write(Path.Combine(dir, name), originalJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not archive the pre-migration copy of {Path}.", path);
        }
    }

    /// <summary>
    /// Snapshot trip.json as it stands, immediately before an import overwrites it.
    ///
    /// Named outside the <c>trip-*.json</c> pattern on purpose: that is what
    /// <see cref="BackupHostedService"/> prunes on a rolling window, and the one copy of the
    /// data an import destroyed is the last file that should age out of the folder on a timer.
    /// </summary>
    private void ArchiveBeforeReplace()
    {
        var path = _options.TripFilePath;
        if (!File.Exists(path)) return;

        try
        {
            Directory.CreateDirectory(_options.BackupDirectory);
            var name = $"replaced-{_clock.UtcNow:yyyyMMdd-HHmmss}.json";
            File.Copy(path, Path.Combine(_options.BackupDirectory, name), overwrite: true);
            _logger.LogInformation("Copied the outgoing trip.json to backups/{Name} before importing.", name);
        }
        catch (Exception ex)
        {
            // Worth knowing about, but not worth refusing the import over — the person asking
            // for it has the file they are importing, which is more than the archive would give.
            _logger.LogError(ex, "Could not archive {Path} before replacing it.", path);
        }
    }

    /// <summary>Never delete a file we failed to parse — rename it so it can be inspected.</summary>
    /// <summary>
    /// Can we actually save? Asked by <see cref="Status"/> rather than assumed, because the
    /// interesting deployment failure is a data directory that reads fine and silently discards
    /// every write — the site looks perfect until it restarts.
    /// </summary>
    private bool DataDirectoryIsWritable()
    {
        try
        {
            Directory.CreateDirectory(_options.DataRoot);
            var probe = Path.Combine(_options.DataRoot, ".write-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void QuarantineUnreadableFile(string path)
    {
        try
        {
            var broken = $"{path}.unreadable-{_clock.UtcNow:yyyyMMdd-HHmmss}";
            File.Move(path, broken, overwrite: true);
            _logger.LogWarning("Moved the unreadable file to {Broken}.", broken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not quarantine {Path}.", path);
        }
    }

    // ---------------------------------------------------------------- writing

    /// <summary>
    /// Restart the debounce timer. Twenty keystrokes in two seconds produce one disk write,
    /// which keeps inline editing from hammering the filesystem.
    /// </summary>
    private void ScheduleFlush()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _debounce, cts);
        CancelAndDispose(previous);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_options.DebounceMilliseconds, cts.Token).ConfigureAwait(false);
                await WriteToDiskAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer edit — that one will do the writing.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Debounced save failed.");
            }
        });
    }

    private void CancelPendingFlush() => CancelAndDispose(Interlocked.Exchange(ref _debounce, null));

    private static void CancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts is null) return;
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        cts.Dispose();
    }

    private async Task WriteToDiskAsync(CancellationToken ct)
    {
        await _fileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string json;
            await _stateGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                json = JsonSerializer.Serialize(_current, TripJson.Options);
            }
            finally
            {
                _stateGate.Release();
            }

            GuardAgainstForeignWrite();

            AtomicFileWriter.Write(_options.TripFilePath, json);
            RememberOurWrite();
            _logger.LogDebug("Saved trip data (revision {Revision}).", _current.Revision);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    /// <summary>
    /// Notice when something outside this process has written trip.json — a hand edit, a
    /// restored backup, or a second instance that should not exist.
    ///
    /// We hold the whole document in memory, so our next save would flatten whatever they did
    /// without a trace. This cannot merge the two, but it refuses to destroy the evidence:
    /// the foreign file is copied into backups/ first, and the incident is logged loudly.
    /// </summary>
    private void GuardAgainstForeignWrite()
    {
        var path = _options.TripFilePath;
        if (_lastWriteWeMade is null || !File.Exists(path)) return;

        DateTime onDisk;
        try { onDisk = File.GetLastWriteTimeUtc(path); }
        catch { return; }

        if (onDisk == _lastWriteWeMade) return;

        _logger.LogWarning(
            "trip.json changed on disk at {OnDisk:o} but our last write was {Ours:o}. " +
            "Something outside this process wrote the file. Archiving it before overwriting. " +
            "If this app is running on more than one instance, stop that now — two writers will lose data.",
            onDisk, _lastWriteWeMade);

        try
        {
            Directory.CreateDirectory(_options.BackupDirectory);
            var name = $"trip-external-{_clock.UtcNow:yyyyMMdd-HHmmss}.json";
            File.Copy(path, Path.Combine(_options.BackupDirectory, name), overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not archive the externally modified file.");
        }
    }

    private void RememberOurWrite()
    {
        try { _lastWriteWeMade = File.GetLastWriteTimeUtc(_options.TripFilePath); }
        catch { _lastWriteWeMade = null; }
    }

    // ------------------------------------------------------------ notification

    private async Task RaiseChangedAsync(TripChanged change)
    {
        var handler = Changed;
        if (handler is null) return;

        // One bad subscriber (a component torn down mid-render, say) must not stop the others.
        foreach (var subscriber in handler.GetInvocationList().Cast<Func<TripChanged, Task>>())
        {
            try
            {
                await subscriber(change).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A subscriber threw handling {Area}.", change.Area);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        CancelPendingFlush();
        try
        {
            await WriteToDiskAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Final save on shutdown failed.");
        }

        _stateGate.Dispose();
        _fileGate.Dispose();
    }
}
