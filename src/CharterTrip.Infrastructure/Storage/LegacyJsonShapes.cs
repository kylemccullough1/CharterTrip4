using System.Text.Json.Nodes;

namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// Repairs shapes the current model cannot read at all, before the deserializer gets a chance
/// to throw on them.
///
/// <see cref="TripMigrations"/> is the right home for almost every model change, but it runs on
/// a <c>TripData</c> — and there is no TripData until deserialization has succeeded. The trip is
/// one document, so a section the reader cannot parse takes the entire file with it: the store
/// quarantines the file and falls back to the seed, and the itinerary, roster and mystery are
/// lost along with the section that actually changed shape.
///
/// Everything here is a fix for one specific historical shape. If a change can be expressed as
/// a migration, write a migration instead.
/// </summary>
internal static class LegacyJsonShapes
{
    /// <summary>Returns true if the document was rewritten and should be re-serialized.</summary>
    public static bool Normalize(JsonNode? root) =>
        root is JsonObject document && DropPreV9JeopardyBoard(document);

    /// <summary>
    /// Before v9 the board listed its categories as bare names and kept every clue in a flat
    /// list beside them; a category now owns its own ordered clues, so the old names no longer
    /// deserialize into <c>JeopardyCategory</c> at all.
    ///
    /// The old board is not salvageable into the new one — the values were rescaled and the clue
    /// text rewritten — and <c>ToV9_JeopardyBoard</c> already replaces an empty board with the
    /// current one. So the section is emptied here and left to that migration, which is what it
    /// was always meant to do and could never actually reach.
    /// </summary>
    private static bool DropPreV9JeopardyBoard(JsonObject document)
    {
        if (document["jeopardy"] is not JsonObject board) return false;
        if (board["categories"] is not JsonArray categories) return false;

        // A v9 board holds objects here. Only the old shape has a bare name.
        if (categories.FirstOrDefault() is not JsonValue first) return false;
        if (!first.TryGetValue<string>(out _)) return false;

        document["jeopardy"] = new JsonObject();
        return true;
    }
}
