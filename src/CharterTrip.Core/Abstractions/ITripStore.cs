using CharterTrip.Core.Models;

namespace CharterTrip.Core.Abstractions;

/// <summary>
/// The one and only way to read or change trip data.
///
/// Reads come straight out of memory — <see cref="Current"/> is the live object, so a page
/// rendering the itinerary never touches the disk. Writes go through <see cref="MutateAsync"/>,
/// which serializes callers, persists, and then tells everyone what changed.
///
/// Keeping this an interface in Core (rather than a concrete file-writing class) is what lets
/// the storage swap to a database later without Core or Web noticing.
/// </summary>
public interface ITripStore
{
    /// <summary>The live trip data. Read freely; never mutate directly — use MutateAsync.</summary>
    TripData Current { get; }

    /// <summary>Where this data came from and whether edits can be saved. See <see cref="TripStoreStatus"/>.</summary>
    TripStoreStatus Status { get; }

    /// <summary>
    /// Apply a change under a lock, persist it, and raise <see cref="Changed"/>.
    /// The <paramref name="area"/> lets subscribers ignore changes they don't care about,
    /// so editing the budget doesn't re-render twenty-six murder mystery cards.
    /// </summary>
    Task MutateAsync(Action<TripData> mutate, TripArea area, CancellationToken ct = default);

    /// <summary>
    /// Replace the entire trip with <paramref name="replacement"/> — the import path.
    ///
    /// Separate from <see cref="MutateAsync"/> because it is a different kind of act: a mutation
    /// edits the document everyone is looking at, while this discards it wholesale. So it saves
    /// immediately rather than on the debounce, keeps a copy of what it overwrote, and announces
    /// itself as <see cref="TripArea.All"/> so every open page re-reads everything.
    ///
    /// <paramref name="replacement"/> is consumed, not kept: its contents are copied onto the
    /// live document so that pages holding <see cref="Current"/> follow the change.
    /// </summary>
    Task ReplaceAsync(TripData replacement, CancellationToken ct = default);

    /// <summary>Force any pending debounced write to disk right now.</summary>
    Task FlushAsync(CancellationToken ct = default);

    /// <summary>Raised after a mutation has been applied. This is the live-update seam.</summary>
    event Func<TripChanged, Task>? Changed;
}

/// <summary>
/// How the store is actually doing, as opposed to whether the process is up.
///
/// A store that seeded because it could not find its file, or that cannot write the file it
/// loaded, serves a complete and entirely convincing site whose edits evaporate on the next
/// restart. That is indistinguishable from a healthy app from the outside, which is exactly why
/// it is worth reporting: <c>Seeded</c> still true after a day of use means the data directory
/// is not surviving deployment.
/// </summary>
/// <param name="DataPath">Resolved absolute path of trip.json, so a misconfigured data root is visible.</param>
/// <param name="Seeded">True if this process started from the built-in seed rather than an existing file.</param>
/// <param name="CanPersist">True if the data directory accepted a write when the store started.</param>
public readonly record struct TripStoreStatus(string DataPath, bool Seeded, bool CanPersist);

public enum TripArea
{
    All,
    Trip,
    Slides,
    Teams,
    Roster,
    Itinerary,
    Games,
    Scores,
    Jeopardy,
    Mystery,
    Venue,
    Travel
}

public readonly record struct TripChanged(TripArea Area, int Revision)
{
    /// <summary>True if a component watching <paramref name="watching"/> should re-render.</summary>
    public bool Affects(TripArea watching) =>
        Area == TripArea.All || watching == TripArea.All || Area == watching;
}
