using System.Text.Json;
using CharterTrip.Core.Models;
using CharterTrip.Infrastructure.Storage;

namespace CharterTrip.Tests;

public class TripMigrationsTests
{
    /// <summary>A trip.json exactly as v1 wrote it: free-text times, no durations, no version.</summary>
    private const string V1Document = """
    {
      "revision": 7,
      "trip": { "name": "Charter Trip", "year": 2026 },
      "itinerary": [
        {
          "id": "fri",
          "day": "Friday",
          "items": [
            { "id": "a", "time": "4:00 PM",  "title": "Check-in",  "tag": "logistics" },
            { "id": "b", "time": "8:00 PM",  "title": "Dinner",    "tag": "food" },
            { "id": "c", "time": "12:00 AM", "title": "Nightcap",  "tag": "freeTime" },
            { "id": "d", "time": "after dinner", "title": "Sketch", "tag": "game" }
          ]
        }
      ]
    }
    """;

    private static TripData LoadV1() =>
        JsonSerializer.Deserialize<TripData>(V1Document, TripJson.Options)!;

    [Fact]
    public void A_v1_document_gains_structured_times()
    {
        var trip = LoadV1();
        Assert.True(TripMigrations.Apply(trip));

        var items = trip.Itinerary[0].Items.ToDictionary(i => i.Id);

        Assert.Equal(16 * 60, items["a"].StartMinutes);
        Assert.Equal(20 * 60, items["b"].StartMinutes);
        Assert.Equal(24 * 60, items["c"].StartMinutes);   // midnight = end of the night
    }

    [Fact]
    public void An_unparseable_time_becomes_an_unscheduled_item_and_keeps_its_text()
    {
        var trip = LoadV1();
        TripMigrations.Apply(trip);

        var sketch = trip.Itinerary[0].Items.Single(i => i.Id == "d");

        Assert.False(sketch.IsScheduled);
        Assert.Equal("after dinner", sketch.TimeNote);
        Assert.Equal("Sketch", sketch.Title);             // nothing was lost
    }

    [Fact]
    public void Durations_are_inferred_from_the_gap_to_the_next_item()
    {
        var trip = LoadV1();
        TripMigrations.Apply(trip);

        var items = trip.Itinerary[0].Items.ToDictionary(i => i.Id);

        // 4pm -> 8pm is four hours, capped at the three-hour maximum.
        Assert.Equal(180, items["a"].DurationMinutes);
        // 8pm -> midnight, also capped.
        Assert.Equal(180, items["b"].DurationMinutes);
        // Last item of the day falls back to an hour.
        Assert.Equal(60, items["c"].DurationMinutes);
    }

    [Fact]
    public void The_legacy_time_field_is_cleared_so_it_stops_being_written()
    {
        var trip = LoadV1();
        TripMigrations.Apply(trip);

        Assert.All(trip.Itinerary[0].Items, i => Assert.Null(i.LegacyTime));

        var rewritten = JsonSerializer.Serialize(trip, TripJson.Options);
        Assert.DoesNotContain("\"time\"", rewritten);
        Assert.Contains("\"startMinutes\"", rewritten);
    }

    [Fact]
    public void The_schema_version_is_stamped()
    {
        var trip = LoadV1();
        Assert.Equal(0, trip.SchemaVersion);

        TripMigrations.Apply(trip);

        Assert.Equal(TripMigrations.CurrentVersion, trip.SchemaVersion);
    }

    [Fact]
    public void Running_it_twice_changes_nothing_the_second_time()
    {
        var trip = LoadV1();
        TripMigrations.Apply(trip);
        var afterFirst = JsonSerializer.Serialize(trip, TripJson.Options);

        Assert.False(TripMigrations.Apply(trip));
        Assert.Equal(afterFirst, JsonSerializer.Serialize(trip, TripJson.Options));
    }

    [Fact]
    public void Items_end_up_in_chronological_order_with_the_tray_last()
    {
        var trip = LoadV1();
        TripMigrations.Apply(trip);

        Assert.Equal(["a", "b", "c", "d"], trip.Itinerary[0].Items.Select(i => i.Id));
    }

    [Fact]
    public void Everything_else_in_the_document_survives()
    {
        var trip = LoadV1();
        TripMigrations.Apply(trip);

        Assert.Equal(7, trip.Revision);
        Assert.Equal("Charter Trip", trip.Trip.Name);
        Assert.Equal(2026, trip.Trip.Year);
    }

    [Fact]
    public void A_document_already_on_v2_is_left_alone()
    {
        var trip = new TripData
        {
            SchemaVersion = TripMigrations.CurrentVersion,
            Itinerary =
            [
                new ItineraryDay
                {
                    Id = "fri",
                    Items = [new ItineraryItem { Id = "a", StartMinutes = 600, DurationMinutes = 45, Title = "Keep me" }]
                }
            ]
        };

        Assert.False(TripMigrations.Apply(trip));
        Assert.Equal(45, trip.Itinerary[0].Items[0].DurationMinutes);
    }
}
