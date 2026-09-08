using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
using Microsoft.Extensions.Logging;

namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// The store a visitor gets in showcase mode: a private copy of the trip that behaves exactly
/// like the real one and is thrown away when they leave.
///
/// The showcase promise is "you can change anything, nothing is saved", and the first attempt at
/// it — a decorator that dropped every write on the floor — made the first half a lie. Buttons
/// did nothing, forms sprang back, and a visitor's reasonable conclusion was that the site was
/// broken rather than frozen. So instead of refusing the write, this takes it: the whole trip is
/// deep-copied at construction and every mutation lands on the copy. The itinerary really does
/// reorder, the scoreboard really does move, Jeopardy really is playable. Nothing reaches
/// <see cref="JsonTripStore"/>, so nothing reaches trip.json.
///
/// The lifetime is the whole mechanism, and it is why this is registered SCOPED while the real
/// store is a singleton. A scope on Blazor Server is a circuit — one browser tab's connection —
/// so the sandbox is born when the tab connects and dies when it disconnects. That is what makes
/// "nothing survives a refresh" true without a line of code about refreshing: a reload is a new
/// circuit, a new scope, and a new copy taken from the untouched baseline. It also means two
/// visitors never see each other's edits, which is the difference between a showcase and a
/// public whiteboard.
///
/// The cost is real and worth naming: two tabs no longer share a game. On the live site the
/// television and the phones are one conversation through <see cref="ITripStore.Changed"/>, and
/// here each tab talks only to itself. There is no way to keep cross-tab sync and still reset on
/// refresh — they are the same piece of state — and the frozen site is the promise being kept.
/// </summary>
public sealed class SandboxTripStore : ITripStore
{
    private readonly TripData _current;
    private readonly TripStoreStatus _status;
    private readonly IClock _clock;
    private readonly ILogger<SandboxTripStore> _logger;

    /// <summary>
    /// Guards the copy, exactly as <see cref="JsonTripStore"/> guards the original.
    ///
    /// A circuit is not single-threaded in the way it looks: a timer callback, an incoming
    /// SignalR message and a background continuation can all be inside a mutation at once, and
    /// the games lean on the store's lock for their "two people pressed it at the same moment"
    /// decisions. Dropping the lock because it is only one visitor would change the semantics of
    /// the very thing being demonstrated.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _writes;

    public SandboxTripStore(JsonTripStore baseline, IClock clock, ILogger<SandboxTripStore> logger)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        _clock = clock;
        _logger = logger;

        // Taken once, here, rather than lazily on the first write. A lazy copy would have to swap
        // the object Current hands out, and pages hold that object between renders — the exact
        // trap TripReplace exists to avoid. Copying up front costs a fraction of a millisecond on
        // a document this size and keeps one identity for the life of the circuit.
        _current = TripJson.Clone(baseline.Current);

        // Honest about itself: the data came from the same file, and it cannot be written back.
        _status = baseline.Status with { CanPersist = false };
    }

    public TripData Current => _current;

    public TripStoreStatus Status => _status;

    public event Func<TripChanged, Task>? Changed;

    public Task MutateAsync(Action<TripData> mutate, TripArea area, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        return MutateAsync<object?>(trip => { mutate(trip); return null; }, area, ct);
    }

    public async Task<T> MutateAsync<T>(Func<TripData, T> mutate, TripArea area, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        TripChanged change;
        T result;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            result = mutate(_current);
            _current.Revision++;
            _current.UpdatedUtc = _clock.UtcNow;
            change = new TripChanged(area, _current.Revision);
        }
        finally
        {
            _gate.Release();
        }

        NoteFirstWrite(area);
        await RaiseChangedAsync(change).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// An import, sandboxed. Worth keeping rather than blocking: the import screen is one of the
    /// more interesting things on the site to show somebody, and here it is completely safe — the
    /// document it overwrites is already a copy.
    /// </summary>
    public async Task ReplaceAsync(TripData replacement, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        TripChanged change;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previous = _current.Revision;

            // Contents, not the reference — same reason as the real store. Every open page on
            // this circuit is holding the object that Current handed it.
            TripReplace.Overwrite(_current, replacement);

            _current.Revision = previous + 1;
            _current.UpdatedUtc = _clock.UtcNow;
            change = new TripChanged(TripArea.All, _current.Revision);
        }
        finally
        {
            _gate.Release();
        }

        NoteFirstWrite(TripArea.All);
        await RaiseChangedAsync(change).ConfigureAwait(false);
    }

    /// <summary>
    /// Nothing to flush, and nothing to forward. Passing this to the real store would write the
    /// untouched baseline back over trip.json — harmless today, and precisely the kind of write
    /// showcase mode exists to guarantee never happens.
    /// </summary>
    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>How many changes this visitor has made to their copy. Useful in a log and in tests.</summary>
    public int WriteCount => _writes;

    private void NoteFirstWrite(TripArea area)
    {
        // One line the first time a visitor edits anything, and silence afterwards. Enough to see
        // in the logs that people are playing with the showcase; not so much that one game of
        // Jeopardy writes a thousand lines.
        if (Interlocked.Increment(ref _writes) == 1)
            _logger.LogInformation("A showcase visitor started editing (first change: {Area}).", area);
    }

    private async Task RaiseChangedAsync(TripChanged change)
    {
        if (Changed is null) return;

        try
        {
            await Changed.Invoke(change).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A subscriber that throws is a page that failed to re-render. It must not take the
            // edit down with it, because the edit has already happened.
            _logger.LogWarning(ex, "A subscriber threw while handling a {Area} change.", change.Area);
        }
    }
}
