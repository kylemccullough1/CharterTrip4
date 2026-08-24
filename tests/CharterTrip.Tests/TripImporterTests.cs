using System.Text.Json;
using System.Text.Json.Nodes;
using CharterTrip.Core.Models;
using CharterTrip.Infrastructure.Seed;
using CharterTrip.Infrastructure.Storage;

namespace CharterTrip.Tests;

/// <summary>
/// The import page shows what a file would do before it does it, so everything worth knowing
/// has to come out of the reader rather than out of the crash afterwards.
/// </summary>
public class TripImporterTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"a string\"")]
    public void Refuses_anything_that_is_not_a_trip_document(string content)
    {
        var report = TripImporter.Read(content);

        Assert.False(report.Ok);
        Assert.NotNull(report.Error);
        Assert.Null(report.Trip);
    }

    /// <summary>
    /// An empty-but-valid document is the dangerous one: it parses, so a naive import would
    /// replace a planned weekend with a blank site and only say so on the projector.
    /// </summary>
    [Fact]
    public void Refuses_a_document_with_no_people_in_it()
    {
        var report = TripImporter.Read("{}");

        Assert.False(report.Ok);
        Assert.Contains("roster", report.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refuses_a_trip_with_nobody_on_a_team()
    {
        var trip = Seed();
        trip["teams"] = new JsonArray();

        var report = TripImporter.Read(trip.ToJsonString());

        Assert.False(report.Ok);
        Assert.Contains("teams", report.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refuses_a_trip_with_no_itinerary()
    {
        var trip = Seed();
        trip["itinerary"] = new JsonArray();

        var report = TripImporter.Read(trip.ToJsonString());

        Assert.False(report.Ok);
        Assert.Contains("itinerary", report.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reads_the_seed_and_counts_what_is_in_it()
    {
        var seed = SeedLoader.Load();
        TripMigrations.Apply(seed);

        var report = TripImporter.Read(JsonSerializer.Serialize(seed, TripJson.Options));

        Assert.True(report.Ok, report.Error);
        Assert.NotNull(report.Trip);
        Assert.Equal(seed.Roster.Count, report.Counts.People);
        Assert.Equal(seed.Teams.Count, report.Counts.Teams);
        Assert.Equal(seed.Itinerary.Count, report.Counts.Days);
        Assert.Equal(seed.Itinerary.Sum(d => d.Items.Count), report.Counts.ItineraryItems);
        Assert.Equal(seed.Jeopardy.Categories.Sum(c => c.Clues.Count), report.Counts.Clues);
        Assert.Empty(report.Warnings);
    }

    /// <summary>
    /// A file exported from an older build is exactly the file someone reaches for when they
    /// want to roll the site back, so it has to import rather than be refused for its age.
    /// </summary>
    [Fact]
    public void Brings_an_older_document_forward_and_says_so()
    {
        var trip = Seed();
        trip["schemaVersion"] = 8;

        var report = TripImporter.Read(trip.ToJsonString());

        Assert.True(report.Ok, report.Error);
        Assert.Equal(8, report.FromSchemaVersion);
        Assert.True(report.WasMigrated);
        Assert.Equal(TripMigrations.CurrentVersion, report.Trip!.SchemaVersion);
    }

    /// <summary>
    /// The pre-v9 board threw inside the deserializer rather than migrating, which is the same
    /// thing that used to quarantine trip.json on load. An import must survive it too.
    /// </summary>
    [Fact]
    public void Repairs_a_pre_v9_board_instead_of_rejecting_the_file()
    {
        var trip = Seed();
        trip["schemaVersion"] = 8;
        trip["jeopardy"] = new JsonObject
        {
            ["categories"] = new JsonArray("KDPhi", "Lambdas"),
            ["values"] = new JsonArray(400, 800)
        };

        var report = TripImporter.Read(trip.ToJsonString());

        Assert.True(report.Ok, report.Error);
        Assert.NotEmpty(report.Trip!.Jeopardy.Categories);
        Assert.NotEmpty(report.Trip.Jeopardy.Categories[0].Clues);
    }

    [Fact]
    public void Warns_when_somebody_is_on_no_team()
    {
        var trip = Seed();
        trip["roster"]![0]!["teamId"] = "a-team-that-does-not-exist";

        var report = TripImporter.Read(trip.ToJsonString());

        Assert.True(report.Ok, report.Error);
        Assert.Contains(report.Warnings, w => w.Contains("no team", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Scores ride along in the file. Nobody uploading an itinerary expects the scoreboard to
    /// change, so the preview has to say it out loud.
    /// </summary>
    [Fact]
    public void Warns_that_an_imported_file_carries_its_own_scoreboard()
    {
        var seed = SeedLoader.Load();
        TripMigrations.Apply(seed);
        seed.Scores.Add(new ScoreEntry { Id = "s1", TeamId = seed.Teams[0].Id, Points = 400 });

        var report = TripImporter.Read(JsonSerializer.Serialize(seed, TripJson.Options));

        Assert.True(report.Ok, report.Error);
        Assert.Contains(report.Warnings, w => w.Contains("score", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Warns_about_an_empty_day()
    {
        var trip = Seed();
        trip["itinerary"]![0]!["items"] = new JsonArray();

        var report = TripImporter.Read(trip.ToJsonString());

        Assert.True(report.Ok, report.Error);
        Assert.Contains(report.Warnings, w => w.Contains("Nothing scheduled", StringComparison.Ordinal));
    }

    /// <summary>The seed, as a mutable document, so a test can break one thing about it.</summary>
    private static JsonObject Seed()
    {
        var seed = SeedLoader.Load();
        TripMigrations.Apply(seed);
        return JsonNode.Parse(JsonSerializer.Serialize(seed, TripJson.Options))!.AsObject();
    }
}
