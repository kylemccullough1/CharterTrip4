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
        root is JsonObject document
        && DropPreV9JeopardyBoard(document) | DropPreV21Mystery(document) | RenameMinglingPhase(document);

    /// <summary>
    /// v34 renamed the party phase: <c>mingling</c> became <c>introductions</c>. The name is written
    /// into a saved file in five places, and the same enum converter that made
    /// <see cref="DropPreV21Mystery"/> necessary throws on the old one — so a trip saved during the
    /// party, or one that ever issued that phase's objective, would quarantine the whole file.
    /// Rewritten here, exactly and only where it is a phase name.
    /// </summary>
    private static bool RenameMinglingPhase(JsonObject document)
    {
        if (document["mystery"] is not JsonObject mystery) return false;

        var changed = Rename(mystery, "phase");

        if (mystery["play"] is JsonObject play)
        {
            changed |= RenameIn(play["objectives"], "issuedInPhase");
            changed |= RenameIn(play["trials"], "phase");
        }

        if (mystery["story"] is JsonObject story)
        {
            changed |= RenameIn(story["objectives"], "phase");

            if (story["factions"] is JsonArray factions)
                foreach (var faction in factions.OfType<JsonObject>())
                    changed |= RenameIn(faction["abilities"], "unlock");
        }

        return changed;

        static bool RenameIn(JsonNode? list, string key)
        {
            if (list is not JsonArray items) return false;

            var changed = false;
            foreach (var item in items.OfType<JsonObject>()) changed |= Rename(item, key);
            return changed;
        }

        static bool Rename(JsonObject owner, string key)
        {
            if (owner[key] is not JsonValue value) return false;
            if (!value.TryGetValue<string>(out var text) || text != "mingling") return false;

            owner[key] = "introductions";
            return true;
        }
    }

    /// <summary>
    /// v21 rebuilt the murder mystery from the model up: a written story plus a phase machine,
    /// where before there was a generated deal and an index into a list of rounds. Nothing in the
    /// old node maps onto the new one.
    ///
    /// Emptying it here rather than in the migration is not tidiness. <c>TripJson.Options</c>
    /// registers a <c>JsonStringEnumConverter</c>, which throws on an enum name it does not know —
    /// and migrations run on a <c>TripData</c>, which means deserialization has already had to
    /// succeed. So one leftover value like <c>"phase": "openVote"</c> in a deployed file does not
    /// break the mystery, it quarantines the entire trip: itinerary, roster, travel and all. Same
    /// reasoning as <see cref="DropPreV9JeopardyBoard"/>, higher stakes.
    /// </summary>
    private static bool DropPreV21Mystery(JsonObject document)
    {
        if (document["mystery"] is not JsonObject mystery) return false;

        // A v21 document has a phase. Only the old shape does not.
        if (mystery["phase"] is not null) return false;
        if (mystery.Count == 0) return false;

        document["mystery"] = new JsonObject();
        return true;
    }

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
