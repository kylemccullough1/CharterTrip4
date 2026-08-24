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
    public void An_unparseable_time_keeps_its_wording_in_the_notes()
    {
        var trip = LoadV1();
        TripMigrations.Apply(trip);

        var sketch = trip.Itinerary[0].Items.Single(i => i.Id == "d");

        // v3 removed the unscheduled state: it lands at midday and the original wording
        // survives in the notes rather than being thrown away.
        Assert.Equal(ItineraryItem.DefaultStartMinutes, sketch.StartMinutes);
        Assert.Contains("after dinner", sketch.Notes);
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
        Assert.DoesNotContain("\"timeNote\"", rewritten);
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
    public void Items_end_up_in_chronological_order()
    {
        var trip = LoadV1();
        TripMigrations.Apply(trip);

        // d lands at midday, so it sorts before the 4pm check-in.
        Assert.Equal(["d", "a", "b", "c"], trip.Itinerary[0].Items.Select(i => i.Id));
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
                    Items = [new ItineraryItem { Id = "a", StartMinutes = 600, DurationMinutes = 45, Title = "Keep me", Version = 3 }]
                }
            ]
        };

        Assert.False(TripMigrations.Apply(trip));
        Assert.Equal(45, trip.Itinerary[0].Items[0].DurationMinutes);
    }

    [Fact]
    public void Every_item_comes_out_of_the_migration_with_a_version()
    {
        var trip = LoadV1();
        TripMigrations.Apply(trip);

        Assert.All(trip.Itinerary.SelectMany(d => d.Items), i => Assert.True(i.Version >= 1));
    }

    [Fact]
    public void No_item_is_left_without_a_start_time()
    {
        var trip = LoadV1();
        TripMigrations.Apply(trip);

        Assert.All(trip.Itinerary.SelectMany(d => d.Items), i => Assert.NotNull(i.StartMinutesOrNull));
    }

    // ------------------------------------------------------------ v5 venue

    private static TripData WithVenue(string checkIn, string checkOut, params string[] outside) => new()
    {
        SchemaVersion = 4,
        Venue = new VenueInfo { CheckIn = checkIn, CheckOut = checkOut, Outside = [.. outside] }
    };

    /// <summary>
    /// Check-in is 2pm, off the travel sheet, which is the booking. v11 briefly moved it to 4pm
    /// on the strength of the guest handbook — but that is the time guests are told to arrive,
    /// not when the property opens — and v12 puts it back. A document run all the way through
    /// comes out at 2pm.
    ///
    /// The checkout aside is still dropped by v5, and still is.
    /// </summary>
    [Fact]
    public void V5_drops_the_checkout_aside_and_check_in_settles_at_2pm()
    {
        var trip = WithVenue("Friday 4:00 PM", "Sunday 12:00 PM (pushed back - thanks Kyle)");
        Assert.True(TripMigrations.Apply(trip));

        Assert.Equal("Friday 2:00 PM", trip.Venue.CheckIn);
        Assert.Equal("Sunday 12:00 PM", trip.Venue.CheckOut);
    }

    /// <summary>The copy v11 left behind on an already-migrated trip is the one v12 exists for.</summary>
    [Fact]
    public void V12_undoes_the_4pm_check_in_v11_wrote()
    {
        var trip = new TripData { SchemaVersion = 11 };
        trip.Venue.CheckIn = "Friday 4:00 PM";
        trip.Guide.Essentials.Add(new GuideFact
        {
            Label = "Check-in",
            Value = "Friday, August 28. The house is ours from 4:00 PM."
        });

        Assert.True(TripMigrations.Apply(trip));

        Assert.Equal("Friday 2:00 PM", trip.Venue.CheckIn);
        Assert.Contains("2:00 PM", trip.Guide.Essentials.Single(f => f.Label == "Check-in").Value);
    }

    /// <summary>
    /// The Friday arrival is moved by id rather than by title. Matching on the word "Check-in"
    /// reached into any trip with an item so named, including ones that have nothing to do with
    /// this weekend.
    /// </summary>
    [Fact]
    public void V12_leaves_an_unrelated_check_in_item_alone()
    {
        var trip = new TripData { SchemaVersion = 11 };
        trip.Itinerary.Add(new ItineraryDay
        {
            Id = "d1",
            Day = "Friday",
            Items = [new ItineraryItem { Id = "something-else", Title = "Check-in", StartMinutes = 960 }]
        });

        TripMigrations.Apply(trip);

        Assert.Equal(960, trip.Itinerary[0].Items[0].StartMinutes);
    }

    [Fact]
    public void V5_shortens_the_swimming_pool_line()
    {
        var trip = WithVenue("Friday 4:00 PM", "Sunday 12:00 PM",
            "Swimming pool (no hot tub)", "Pond - catch & release fishing allowed", "Grill");

        TripMigrations.Apply(trip);

        Assert.Equal(["Swimming pool", "Pond - catch & release fishing allowed", "Grill"], trip.Venue.Outside);
    }

    [Fact]
    public void V5_leaves_values_that_have_already_been_corrected()
    {
        var trip = WithVenue("Friday 2:00 PM", "Sunday 12:00 PM", "Swimming pool");
        trip.SchemaVersion = TripMigrations.CurrentVersion;

        Assert.False(TripMigrations.Apply(trip));
        Assert.Equal("Friday 2:00 PM", trip.Venue.CheckIn);
        Assert.Equal(["Swimming pool"], trip.Venue.Outside);
    }

    [Fact]
    public void V5_does_not_stamp_over_a_hand_edited_check_in()
    {
        var trip = WithVenue("Friday 3:30 PM", "Sunday 11:00 AM", "Swimming pool");
        TripMigrations.Apply(trip);

        Assert.Equal("Friday 3:30 PM", trip.Venue.CheckIn);
        Assert.Equal("Sunday 11:00 AM", trip.Venue.CheckOut);
    }

    // ------------------------------------------------------------ v7 countdown

    [Fact]
    public void V7_moves_the_countdown_target_to_2pm()
    {
        var trip = new TripData
        {
            SchemaVersion = 6,
            Trip = { StartsAt = new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.FromHours(-5)) }
        };

        Assert.True(TripMigrations.Apply(trip));
        Assert.Equal(14, trip.Trip.StartsAt.Hour);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 14, 0, 0, TimeSpan.FromHours(-5)), trip.Trip.StartsAt);
    }

    [Fact]
    public void V7_leaves_a_start_time_that_is_not_the_old_4pm_alone()
    {
        var trip = new TripData
        {
            SchemaVersion = 6,
            Trip = { StartsAt = new DateTimeOffset(2026, 8, 28, 18, 30, 0, TimeSpan.FromHours(-5)) }
        };

        TripMigrations.Apply(trip);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 18, 30, 0, TimeSpan.FromHours(-5)), trip.Trip.StartsAt);
    }

    // ---------------------------------------------------------- v8 short names

    private static TripData LongNames() => new()
    {
        SchemaVersion = 7,
        Teams =
        [
            new Team { Id = "jou",  Name = "Team Jou",  Lead = "JouJou" },
            new Team { Id = "kyle", Name = "Team Kyle", Lead = "Kyle McCullough" }
        ],
        Roster =
        [
            new RosterPerson { Id = "p-kyle-mccullough", Name = "Kyle McCullough", TeamId = "kyle" },
            new RosterPerson { Id = "p-maria-riri",      Name = "Maria Riri",      TeamId = "kyle" },
            new RosterPerson { Id = "p-justin-brown",    Name = "Justin Brown",    TeamId = "kyle" }
        ]
    };

    [Fact]
    public void V8_shortens_the_roster_names()
    {
        var trip = LongNames();
        Assert.True(TripMigrations.Apply(trip));

        Assert.Equal(["Kyle", "Riri", "JB"], trip.Roster.Select(p => p.Name));
    }

    [Fact]
    public void V8_keeps_a_team_pointing_at_its_lead_through_the_rename()
    {
        var trip = LongNames();
        TripMigrations.Apply(trip);

        Assert.Equal("Kyle", trip.Teams.Single(t => t.Id == "kyle").Lead);
    }

    [Fact]
    public void V8_renames_Team_Jou_to_match_its_lead()
    {
        var trip = LongNames();
        TripMigrations.Apply(trip);

        Assert.Equal("Team JouJou", trip.Teams.Single(t => t.Id == "jou").Name);
    }

    [Fact]
    public void V8_leaves_a_person_it_does_not_recognise_alone()
    {
        var trip = LongNames();
        trip.Roster.Add(new RosterPerson { Id = "p-someone-new", Name = "Someone New", TeamId = "kyle" });

        TripMigrations.Apply(trip);

        Assert.Equal("Someone New", trip.Roster.Single(p => p.Id == "p-someone-new").Name);
    }

    [Fact]
    public void V8_running_twice_changes_nothing_the_second_time()
    {
        var trip = LongNames();
        TripMigrations.Apply(trip);

        Assert.False(TripMigrations.Apply(trip));
        Assert.Equal(["Kyle", "Riri", "JB"], trip.Roster.Select(p => p.Name));
    }

    // --------------------------------------------------------- v11: the guide

    /// <summary>
    /// The deployed trip has no guide at all, so the whole handbook arrives by migration rather
    /// than needing somebody to import a file before the site is any use.
    /// </summary>
    [Fact]
    public void V11_fills_an_empty_guide_from_the_seed()
    {
        var trip = new TripData { SchemaVersion = 10 };

        Assert.True(TripMigrations.Apply(trip));

        Assert.NotEmpty(trip.Guide.Essentials);
        Assert.NotEmpty(trip.Guide.Menu);
        Assert.NotEmpty(trip.Guide.Packing);
        Assert.NotEmpty(trip.Guide.CarBrings);
        Assert.False(string.IsNullOrWhiteSpace(trip.Guide.DressCode));
    }

    /// <summary>Exactly one row shouts, and it is the one that costs money to miss.</summary>
    [Fact]
    public void V11_marks_only_the_not_covered_row_as_loud()
    {
        var trip = new TripData { SchemaVersion = 10 };
        TripMigrations.Apply(trip);

        var loud = trip.Guide.Essentials.Where(f => f.Highlight).ToList();

        Assert.Single(loud);
        Assert.Equal("Not covered", loud[0].Label);
    }

    /// <summary>
    /// A ticked box is remembered against an item's id, so the ids have to be stable and unique.
    /// Generated ones would differ per environment and empty everyone's list on the first import.
    /// </summary>
    [Fact]
    public void V11_packing_items_all_carry_a_distinct_id()
    {
        var trip = new TripData { SchemaVersion = 10 };
        TripMigrations.Apply(trip);

        var ids = trip.Guide.Packing.SelectMany(g => g.Items).Select(i => i.Id).ToList();

        Assert.NotEmpty(ids);
        Assert.DoesNotContain(ids, string.IsNullOrWhiteSpace);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void V11_leaves_a_guide_that_has_already_been_written()
    {
        var trip = new TripData { SchemaVersion = 10 };
        trip.Guide.Essentials.Add(new GuideFact { Label = "Where", Value = "Somewhere else entirely" });

        TripMigrations.Apply(trip);

        Assert.Equal("Somewhere else entirely", Assert.Single(trip.Guide.Essentials).Value);
    }

    /// <summary>The correction is guarded on the stale value, so a deliberate time is not stamped over.</summary>
    [Fact]
    public void V11_does_not_touch_a_check_in_somebody_chose()
    {
        var trip = new TripData { SchemaVersion = 10 };
        trip.Venue.CheckIn = "Friday 6:30 PM";

        TripMigrations.Apply(trip);

        Assert.Equal("Friday 6:30 PM", trip.Venue.CheckIn);
    }

    [Fact]
    public void V11_running_twice_changes_nothing_the_second_time()
    {
        var trip = new TripData { SchemaVersion = 10 };
        TripMigrations.Apply(trip);

        Assert.False(TripMigrations.Apply(trip));
    }
}
