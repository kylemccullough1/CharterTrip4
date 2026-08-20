using System.Text.Json;
using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
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

    public JsonTripStore(IOptions<TripStoreOptions> options, ILogger<JsonTripStore> logger, IClock clock)
    {
        _options = options.Value;
        _logger = logger;
        _clock = clock;
        _current = LoadOrSeed();
    }

    public TripData Current => _current;

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
                var loaded = JsonSerializer.Deserialize<TripData>(json, TripJson.Options);
                if (loaded is not null)
                {
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
        seeded.UpdatedUtc = _clock.UtcNow;

        try
        {
            AtomicFileWriter.Write(path, JsonSerializer.Serialize(seeded, TripJson.Options));
            _logger.LogInformation("No trip data found — wrote a fresh seed to {Path}.", path);
        }
        catch (Exception ex)
        {
            // Running with a read-only data directory is survivable: we just can't persist.
            _logger.LogError(ex, "Seeded in memory but could not write {Path}.", path);
        }

        return seeded;
    }

    /// <summary>Never delete a file we failed to parse — rename it so it can be inspected.</summary>
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

            AtomicFileWriter.Write(_options.TripFilePath, json);
            _logger.LogDebug("Saved trip data (revision {Revision}).", _current.Revision);
        }
        finally
        {
            _fileGate.Release();
        }
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
