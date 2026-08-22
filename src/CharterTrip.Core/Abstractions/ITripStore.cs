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

    /// <summary>
    /// Apply a change under a lock, persist it, and raise <see cref="Changed"/>.
    /// The <paramref name="area"/> lets subscribers ignore changes they don't care about,
    /// so editing the budget doesn't re-render twenty-six murder mystery cards.
    /// </summary>
    Task MutateAsync(Action<TripData> mutate, TripArea area, CancellationToken ct = default);

    /// <summary>Force any pending debounced write to disk right now.</summary>
    Task FlushAsync(CancellationToken ct = default);

    /// <summary>Raised after a mutation has been applied. This is the live-update seam.</summary>
    event Func<TripChanged, Task>? Changed;
}

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
    Venue
}

public readonly record struct TripChanged(TripArea Area, int Revision)
{
    /// <summary>True if a component watching <paramref name="watching"/> should re-render.</summary>
    public bool Affects(TripArea watching) =>
        Area == TripArea.All || watching == TripArea.All || Area == watching;
}
