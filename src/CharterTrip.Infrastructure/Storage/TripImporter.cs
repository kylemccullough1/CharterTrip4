using System.Text.Json;
using System.Text.Json.Nodes;
using CharterTrip.Core.Models;

namespace CharterTrip.Infrastructure.Storage;

/// <summary>
/// Reads an uploaded trip.json the same way the store reads the one on disk — legacy shapes
/// repaired, migrations applied — and then says whether it is safe to make live.
///
/// The store's own loader can afford to be forgiving: a file it cannot read gets quarantined
/// and the seed takes over, and the site still comes up. An import has no such fallback. It
/// overwrites a working trip on purpose, so the checking happens here, before anything is
/// replaced, and the answer is shown to a person who can still click Cancel.
///
/// Nothing in here touches the store. <see cref="TripImportReport.Trip"/> is a loose document
/// the caller may hand to <c>ReplaceAsync</c> — or throw away.
/// </summary>
public static class TripImporter
{
    /// <summary>Parse, migrate and check an uploaded document. Never throws for bad input.</summary>
    public static TripImportReport Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return TripImportReport.Failed("That file is empty.");

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return TripImportReport.Failed($"That file is not valid JSON — {ex.Message}");
        }

        if (node is not JsonObject)
            return TripImportReport.Failed("That JSON is not a trip — the file should be a { } object.");

        TripData? trip;
        try
        {
            // The same repair pass the store runs on load, so a file exported from an older
            // build imports rather than being refused over a section that is about to be
            // replaced anyway.
            if (LegacyJsonShapes.Normalize(node))
                json = node.ToJsonString(TripJson.Options);

            trip = JsonSerializer.Deserialize<TripData>(json, TripJson.Options);
        }
        catch (JsonException ex)
        {
            return TripImportReport.Failed($"That file is JSON, but not a trip this app can read — {ex.Message}");
        }

        if (trip is null)
            return TripImportReport.Failed("That file read as nothing at all.");

        var fromVersion = trip.SchemaVersion;
        TripMigrations.Apply(trip);

        // Hard stops: things the site cannot render at all. Better to refuse the file than to let
        // someone replace a working trip with an empty one and find out on the projector.
        if (trip.Roster.Count == 0)
            return TripImportReport.Failed("That trip has nobody in the roster. Refusing to import it.");

        if (trip.Teams.Count == 0)
            return TripImportReport.Failed("That trip has no teams. Refusing to import it.");

        if (trip.Itinerary.Count == 0)
            return TripImportReport.Failed("That trip has no itinerary days. Refusing to import it.");

        return new TripImportReport
        {
            Ok = true,
            Trip = trip,
            FromSchemaVersion = fromVersion,
            FileRevision = trip.Revision,
            FileUpdatedUtc = trip.UpdatedUtc,
            Counts = TripCounts.Of(trip),
            Warnings = Check(trip)
        };
    }

    /// <summary>
    /// Everything worth a second look that is not worth blocking on. These are shown next to the
    /// confirm button, because the person uploading is the only one who can say whether a board
    /// with four categories is a mistake or a Saturday they shortened.
    /// </summary>
    private static List<string> Check(TripData trip)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(trip.Trip.Name))
            warnings.Add("The trip has no name.");

        var teamIds = trip.Teams.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var orphans = trip.Roster.Where(p => !teamIds.Contains(p.TeamId)).Select(p => p.Name).ToList();
        if (orphans.Count > 0)
            warnings.Add($"{People(orphans.Count)} on no team: {string.Join(", ", orphans.Take(5))}" +
                         (orphans.Count > 5 ? ", and others" : ""));

        var itemIds = trip.Itinerary.SelectMany(d => d.Items).Select(i => i.Id).ToList();
        var duplicates = itemIds.Count - itemIds.Distinct(StringComparer.Ordinal).Count();
        if (duplicates > 0)
            warnings.Add($"{duplicates} itinerary item{(duplicates == 1 ? " shares its id" : "s share ids")} " +
                         "with another. Editing one may move the other.");

        var emptyDays = trip.Itinerary.Where(d => d.Items.Count == 0).Select(d => d.Day).ToList();
        if (emptyDays.Count > 0)
            warnings.Add($"Nothing scheduled on {string.Join(", ", emptyDays)}.");

        if (trip.Jeopardy.Categories.Count == 0)
        {
            warnings.Add("The Jeopardy board is empty.");
        }
        else
        {
            var thin = trip.Jeopardy.Categories.Where(c => c.Clues.Count == 0).Select(c => c.Name).ToList();
            if (thin.Count > 0)
                warnings.Add($"Jeopardy categories with no clues: {string.Join(", ", thin)}.");
        }

        // A mystery file carries its own solution — who is guilty is written into the story, not
        // drawn on the night — so importing one mid-evening swaps the murder out from under the room.
        // Worth saying plainly rather than letting it be discovered on the wall.
        if (trip.Mystery.Story.Characters.Count > 0)
        {
            var killers = trip.Mystery.Story.Killers.Count();
            var seated = trip.Mystery.Play.Cast.Count(c => c.PersonId is not null);
            warnings.Add(
                $"This file carries a murder mystery: {trip.Mystery.Story.Characters.Count} characters " +
                $"and {killers} killers, replacing the current story." +
                (seated > 0 ? $" {seated} people are already cast in it." : ""));
        }

        // Play state travels with the file. Nobody expects an upload to change the scoreboard,
        // so say so here rather than letting it be discovered on the wall.
        if (trip.Scores.Count > 0)
            warnings.Add($"This file carries {trip.Scores.Count} score {(trip.Scores.Count == 1 ? "entry" : "entries")} — " +
                         "importing puts that scoreboard up.");

        if (trip.Jeopardy.Game.Phase != JeopardyPhase.NotStarted)
            warnings.Add($"This file has a Jeopardy game in progress ({trip.Jeopardy.Game.Phase}), " +
                         "including its buzzer codes.");

        if (trip.Mystery.Phase != MysteryPhase.Lobby)
            warnings.Add($"This file has the murder mystery running ({trip.Mystery.Phase}).");

        return warnings;
    }

    private static string People(int count) => count == 1 ? "1 person is" : $"{count} people are";
}

/// <summary>The verdict on one uploaded file, and the document itself if it passed.</summary>
public sealed record TripImportReport
{
    public bool Ok { get; init; }

    /// <summary>Why the file was refused. Null when <see cref="Ok"/>.</summary>
    public string? Error { get; init; }

    /// <summary>The migrated document, ready for <c>ReplaceAsync</c>. Null when the file failed.</summary>
    public TripData? Trip { get; init; }

    /// <summary>The schema the file was written in, before migrations ran on it.</summary>
    public int FromSchemaVersion { get; init; }

    /// <summary>The revision recorded in the file — whatever the app that wrote it had reached.</summary>
    public int FileRevision { get; init; }

    public DateTimeOffset FileUpdatedUtc { get; init; }

    public TripCounts Counts { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True if the file came from an older build and had to be brought forward.</summary>
    public bool WasMigrated => Ok && FromSchemaVersion != TripMigrations.CurrentVersion;

    public static TripImportReport Failed(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// The handful of numbers that tell you at a glance whether a file is the trip you think it is.
/// Shown beside the live trip's own, so an import that drops nine people is visible before it
/// happens rather than afterwards.
/// </summary>
public readonly record struct TripCounts(
    int People,
    int Teams,
    int Days,
    int ItineraryItems,
    int Clues,
    int MysteryRoles,
    int Scores)
{
    public static TripCounts Of(TripData trip) => new(
        trip.Roster.Count,
        trip.Teams.Count,
        trip.Itinerary.Count,
        trip.Itinerary.Sum(d => d.Items.Count),
        trip.Jeopardy.Categories.Sum(c => c.Clues.Count),
        trip.Mystery.Story.Characters.Count,
        trip.Scores.Count);
}
