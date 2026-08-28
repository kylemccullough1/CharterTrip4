using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// Overwrites one trip with another, field by field, in place.
///
/// The obvious implementation of an import is to swap the store's reference for the new
/// document. That breaks quietly and completely: <see cref="Abstractions.ITripStore.Current"/>
/// hands out the live object, and every open page is holding it. Swap the reference and those
/// pages keep rendering — and keep editing — a document that nothing saves any more, while the
/// import appears to have worked. So the identity of the object is preserved and its contents
/// are replaced; the pages re-render against the same instance they always had.
///
/// Revision and UpdatedUtc are deliberately not copied. They describe this store's history,
/// not the imported file's — the store stamps them itself.
/// </summary>
public static class TripReplace
{
    public static void Overwrite(TripData target, TripData source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        target.SchemaVersion = source.SchemaVersion;

        target.Trip = source.Trip;
        target.Venue = source.Venue;
        target.Guide = source.Guide;
        target.Travel = source.Travel;
        target.PlannerPxPerHour = source.PlannerPxPerHour;

        target.Slides = source.Slides;
        target.Teams = source.Teams;
        target.Roster = source.Roster;
        target.Itinerary = source.Itinerary;
        target.Games = source.Games;
        target.Scores = source.Scores;
        target.Superlatives = source.Superlatives;
        target.Jeopardy = source.Jeopardy;
        target.SpellingBee = source.SpellingBee;
        target.Mystery = source.Mystery;
        target.Party = source.Party;
    }
}
