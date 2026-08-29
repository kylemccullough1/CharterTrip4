using System.Text.Json;
using CharterTrip.Core.Models;
using CharterTrip.Core.Words;
using CharterTrip.Infrastructure.Mystery;
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
        Assert.NotEmpty(trip.Guide.MenuDays);
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

        // Seeded as "Not covered"; v14 renames it on the way past and the flag rides along.
        Assert.Equal("What's Not Covered", loud[0].Label);
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

    /// <summary>
    /// A guide somebody has written is not replaced by the seed's. v14 does then sort that row
    /// under a heading and add the blank Payment line beside it, which is its job — what matters
    /// here is that the value written by hand survives untouched.
    /// </summary>
    [Fact]
    public void V11_leaves_a_guide_that_has_already_been_written()
    {
        var trip = new TripData { SchemaVersion = 10 };
        trip.Guide.Essentials.Add(new GuideFact { Label = "Where", Value = "Somewhere else entirely" });

        TripMigrations.Apply(trip);

        var where = Assert.Single(trip.Guide.Essentials, f => f.Label == "Where");
        Assert.Equal("Somewhere else entirely", where.Value);

        // The seed's seven rows did not arrive alongside it.
        Assert.DoesNotContain(trip.Guide.Essentials, f => f.Label == "Bedrooms");
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

    // ----------------------------------------------------- v13: menu board

    [Fact]
    public void V13_fills_the_menu_board_from_the_seed()
    {
        var trip = new TripData { SchemaVersion = 12 };

        Assert.True(TripMigrations.Apply(trip));

        Assert.Equal(3, trip.Guide.MenuDays.Count);
        Assert.NotEmpty(trip.Guide.Staples);
        Assert.All(trip.Guide.MenuDays, d => Assert.False(string.IsNullOrWhiteSpace(d.Id)));

        // Saturday is the full day; the seed should say so.
        var saturday = trip.Guide.MenuDays.Single(d => d.Day == "Saturday");
        Assert.False(string.IsNullOrWhiteSpace(saturday.Breakfast));
        Assert.False(string.IsNullOrWhiteSpace(saturday.Lunch));
        Assert.False(string.IsNullOrWhiteSpace(saturday.Dinner));
    }

    /// <summary>A board somebody has already rearranged is not overwritten by the seed's.</summary>
    [Fact]
    public void V13_leaves_a_menu_board_that_has_been_worked_on()
    {
        var trip = new TripData { SchemaVersion = 12 };
        trip.Guide.MenuDays.Add(new MenuDay { Id = "custom", Day = "Thursday", Dinner = "Tacos" });

        TripMigrations.Apply(trip);

        Assert.Equal("Tacos", Assert.Single(trip.Guide.MenuDays).Dinner);
    }

    // ------------------------------------------------ v14: grouped essentials

    /// <summary>A trip carrying the flat v11 essentials, exactly as they were seeded.</summary>
    private static TripData Ungrouped()
    {
        var trip = new TripData { SchemaVersion = 13 };
        trip.Guide.Essentials.AddRange(
        [
            new GuideFact { Label = "Where", Value = "Braun Manor" },
            new GuideFact { Label = "Check-in", Value = "Friday" },
            new GuideFact { Label = "Check-out", Value = "Sunday" },
            new GuideFact { Label = "Covered", Value = "Meals" },
            new GuideFact { Label = "Not covered", Value = "Beer", Highlight = true },
            new GuideFact { Label = "Bedrooms", Value = "8" },
            new GuideFact { Label = "Bathrooms", Value = "4" }
        ]);

        return trip;
    }

    [Fact]
    public void V14_sorts_the_essentials_under_headings()
    {
        var trip = Ungrouped();

        Assert.True(TripMigrations.Apply(trip));

        string Group(string label) =>
            trip.Guide.Essentials.Single(f => f.Label == label).Group;

        Assert.Equal("Logistics", Group("Where"));
        Assert.Equal("Logistics", Group("Check-in"));
        Assert.Equal("Logistics", Group("Check-out"));
        Assert.Equal("Financial", Group("What's Covered"));
        Assert.Equal("Financial", Group("What's Not Covered"));
        Assert.Equal("The house", Group("Bedrooms"));
        Assert.Equal("The house", Group("Bathrooms"));
    }

    /// <summary>Payment is new, blank, and sits with the rest of the money.</summary>
    [Fact]
    public void V14_adds_a_payment_line_to_the_financial_group()
    {
        var trip = Ungrouped();
        TripMigrations.Apply(trip);

        var payment = trip.Guide.Essentials.Single(f => f.Label == "Payment");

        Assert.Equal("Financial", payment.Group);
        Assert.Equal("", payment.Value);

        // Immediately after the other Financial rows, not stranded at the end of the list.
        var financial = trip.Guide.Essentials
            .Select((f, i) => (f, i))
            .Where(x => x.f.Group == "Financial")
            .Select(x => x.i)
            .ToList();

        Assert.Equal(financial.Count, financial.Max() - financial.Min() + 1);
    }

    /// <summary>Renaming must not lose the one row allowed to shout.</summary>
    [Fact]
    public void V14_keeps_the_highlight_on_the_renamed_not_covered_row()
    {
        var trip = Ungrouped();
        TripMigrations.Apply(trip);

        var loud = Assert.Single(trip.Guide.Essentials, f => f.Highlight);
        Assert.Equal("What's Not Covered", loud.Label);
    }

    [Fact]
    public void V14_leaves_headings_that_have_already_been_arranged()
    {
        var trip = new TripData { SchemaVersion = 13 };
        trip.Guide.Essentials.Add(new GuideFact { Group = "Mine", Label = "Where", Value = "Elsewhere" });

        TripMigrations.Apply(trip);

        Assert.Equal("Mine", Assert.Single(trip.Guide.Essentials).Group);
    }

    // ------------------------------- v24/v25: the beer run is a race to four

    [Fact]
    public void V24_gives_the_beer_run_the_number_that_wins_a_round()
    {
        var trip = new TripData { SchemaVersion = 23 };

        Assert.True(TripMigrations.Apply(trip));

        Assert.Equal(4, trip.Party.BeerRun.TakeToWin);
        Assert.Null(trip.Party.NoodleCup.TakeToWin);
    }

    /// <summary>
    /// The party steps were numbered from 19 on the branch they were written on, where the bee did
    /// not exist. A file the bee has already stamped 21 is exactly what that numbering would have
    /// stranded — every party step behind it, none of them ever run. This is the test that says
    /// they were moved to the end of the ladder rather than left where they were.
    /// </summary>
    [Fact]
    public void The_party_games_reach_a_file_the_bee_already_stamped()
    {
        var trip = new TripData { SchemaVersion = 21 };

        Assert.True(TripMigrations.Apply(trip));

        Assert.NotEqual(0, trip.Party.Sketch.PointValue);
        Assert.NotEmpty(trip.Party.Sketch.Characters);
        Assert.Equal(4, trip.Party.BeerRun.TakeToWin);
    }

    /// <summary>A number the host has already chosen is theirs, not the seed's.</summary>
    [Fact]
    public void V24_leaves_a_number_to_win_that_has_already_been_set()
    {
        var trip = new TripData { SchemaVersion = 23 };
        trip.Party.BeerRun.TakeToWin = 3;

        TripMigrations.Apply(trip);

        Assert.Equal(3, trip.Party.BeerRun.TakeToWin);
    }

    /// <summary>
    /// v25 carries no data of its own — it exists to restamp the file so the roundPool key the
    /// earlier shape wrote is flushed rather than sitting there ignored. So it must still report
    /// a change.
    /// </summary>
    [Fact]
    public void V25_restamps_a_file_that_still_carries_the_old_stack_key()
    {
        var trip = new TripData { SchemaVersion = 24 };

        Assert.True(TripMigrations.Apply(trip));
        Assert.Equal(TripMigrations.CurrentVersion, trip.SchemaVersion);
    }

    [Fact]
    public void The_beer_run_migrations_run_twice_without_changing_anything()
    {
        var trip = new TripData { SchemaVersion = 23 };
        TripMigrations.Apply(trip);

        Assert.False(TripMigrations.Apply(trip));
    }

    [Fact]
    public void V14_running_twice_changes_nothing_the_second_time()
    {
        var trip = Ungrouped();
        TripMigrations.Apply(trip);

        Assert.False(TripMigrations.Apply(trip));
    }

    /// <summary>
    /// The bee stopped shipping a word list and started dealing one. A file written before that
    /// holds forty-five hand-written words with hints and no tier — a shape the new deck cannot
    /// represent — so they go, and the next Start draws two hundred real ones.
    /// </summary>
    [Fact]
    public void V20_clears_the_old_hand_written_word_list()
    {
        var trip = new TripData { SchemaVersion = 19 };
        trip.SpellingBee.Words.Add(new BeeWord { Id = "old", Word = "rhythm" });

        TripMigrations.Apply(trip);

        Assert.Empty(trip.SpellingBee.Words);
    }

    [Fact]
    public void V20_arrives_with_per_word_scoring()
    {
        var trip = new TripData { SchemaVersion = 19 };

        TripMigrations.Apply(trip);

        Assert.Equal(5, trip.SpellingBee.PointsPerWord);
    }

    /// <summary>
    /// v21 traded the dealt deck for one dial. A file written before it has no difficulty on it
    /// at all — or, if it was hand-edited, something that is not a tier — and either way the bee
    /// has to come up pointing at a real tier rather than drawing from nowhere.
    /// </summary>
    [Fact]
    public void V21_lands_on_a_real_difficulty_whatever_the_old_file_said()
    {
        var trip = new TripData { SchemaVersion = 20 };
        trip.SpellingBee.DifficultyKey = "nonsense";

        TripMigrations.Apply(trip);

        var bee = trip.SpellingBee;
        Assert.True(WordBank.IsTier(bee.DifficultyKey), $"'{bee.DifficultyKey}' is not a tier");
        Assert.Equal(bee.DifficultyKey, bee.Game.DifficultyKey);
    }

    /// <summary>
    /// A v20 Words list is a dealt deck — words nobody has read yet. Under v21 the same list
    /// means the opposite: every word already spent. Carrying it across would retire two hundred
    /// words on the first turn, so it goes.
    /// </summary>
    [Fact]
    public void V21_throws_away_the_undealt_deck_rather_than_calling_it_spent()
    {
        var trip = new TripData { SchemaVersion = 20 };
        trip.SpellingBee.Words.Add(new BeeWord { Id = "sb-1", Word = "rhythm", TierKey = "moderate" });
        trip.SpellingBee.Game.Phase = BeePhase.Spelling;

        TripMigrations.Apply(trip);

        Assert.Empty(trip.SpellingBee.Words);
        Assert.Equal(BeePhase.NotStarted, trip.SpellingBee.Game.Phase);
    }

    /// <summary>
    /// A game saved mid-v19 rotated by team through a Survivors queue, which has no counterpart
    /// in a fixed shuffled row. Resuming it would mean inventing an order nobody stood in, so it
    /// goes back to the title card — and its points go with it, because they were awarded once at
    /// the end under a rule that no longer exists.
    /// </summary>
    [Fact]
    public void V20_sends_a_half_played_bee_back_to_the_title_card()
    {
        var trip = new TripData { SchemaVersion = 19 };
        trip.SpellingBee.Game.Phase = BeePhase.Spelling;
        trip.SpellingBee.Game.CurrentPersonId = "p-someone";
        trip.Scores.Add(new ScoreEntry { Id = "sc-old", GameId = "spelling", TeamId = "a", Points = 10 });
        trip.Scores.Add(new ScoreEntry { Id = "sc-keep", GameId = "jeopardy", TeamId = "a", Points = 15 });

        TripMigrations.Apply(trip);

        var game = trip.SpellingBee.Game;
        Assert.Equal(BeePhase.NotStarted, game.Phase);
        Assert.Null(game.CurrentPersonId);
        Assert.Empty(game.Order);

        Assert.Equal("sc-keep", Assert.Single(trip.Scores).Id);
    }

    [Fact]
    public void V20_freshens_the_bees_rules_because_the_game_changed()
    {
        var trip = new TripData { SchemaVersion = 19 };
        trip.Games.Add(new Game { Id = "spelling", Name = "Spelling Bee", Rules = ["Alternating order across teams."] });

        TripMigrations.Apply(trip);

        var rules = trip.Games.Single(g => g.Id == "spelling").Rules;
        Assert.DoesNotContain(rules, r => r.Contains("Alternating"));
        Assert.Contains(rules, r => r.Contains("shuffled into one row"));
    }

    /// <summary>
    /// A file saved while the Newlywed Game was still on the list loses the card and the Friday
    /// slot it held, and nothing standing next to either of them moves.
    /// </summary>
    [Fact]
    public void V26_takes_the_newlywed_game_off_the_list_and_off_friday()
    {
        var trip = NewlywedStillListed();

        Assert.True(TripMigrations.Apply(trip));

        Assert.DoesNotContain(trip.Games, g => g.Id == "newlywed");
        Assert.Equal(["jeopardy", "sketch"], trip.Games.Select(g => g.Id));

        var friday = trip.Itinerary.Single().Items;
        Assert.DoesNotContain(friday, i => i.Title.Contains("Newlywed"));
        Assert.Equal(["item-f4", "item-f6"], friday.Select(i => i.Id));
    }

    /// <summary>
    /// The slot goes by what it says rather than by the id it happens to carry, because an
    /// itinerary item is dragged and renumbered and a game card is not.
    /// </summary>
    [Fact]
    public void V26_finds_the_friday_slot_even_when_it_has_been_renumbered()
    {
        var trip = NewlywedStillListed();
        trip.Itinerary[0].Items[1].Id = "item-f9";

        TripMigrations.Apply(trip);

        Assert.DoesNotContain(trip.Itinerary[0].Items, i => i.Title.Contains("Newlywed"));
    }

    /// <summary>
    /// Taking the card off the page is not taking back what a team won. The log is append-only
    /// and the standings are its sum, so an entry against the game outlives the game.
    /// </summary>
    [Fact]
    public void V26_leaves_points_already_awarded_where_they_are()
    {
        var trip = NewlywedStillListed();
        trip.Scores.Add(new ScoreEntry { Id = "sc-1", GameId = "newlywed", TeamId = "jou", Points = 10 });

        TripMigrations.Apply(trip);

        Assert.Equal("sc-1", Assert.Single(trip.Scores).Id);
    }

    [Fact]
    public void V26_running_twice_changes_nothing_the_second_time()
    {
        var trip = NewlywedStillListed();
        TripMigrations.Apply(trip);

        Assert.False(TripMigrations.Apply(trip));
    }

    /// <summary>
    /// A v18 document as it stood before phase 2: nobody has a join token, and the mystery is
    /// still West Egg Manor, complete with the properties the model no longer has.
    /// </summary>
    private const string V18WestEggDocument = """
    {
      "schemaVersion": 18,
      "trip": { "name": "Charter Trip", "year": 2026 },
      "roster": [
        { "id": "p1", "name": "Kyle", "teamId": "t1", "role": "admin" },
        { "id": "p2", "name": "JB",   "teamId": "t1" }
      ],
      "teams": [ { "id": "t1", "name": "The Lambdas" } ],
      "slides": [ { "id": "s3", "kind": "deco", "caption": "Murder at West Egg Manor" } ],
      "games": [ { "id": "g1", "name": "Murder Mystery - Murder at West Egg Manor" } ],
      "itinerary": [
        {
          "id": "sat",
          "day": "Saturday",
          "items": [
            { "id": "i1", "startMinutes": 1200, "durationMinutes": 120,
              "title": "Murder Mystery - Murder at West Egg Manor", "tag": "game" }
          ]
        }
      ],
      "mystery": {
        "title": "Murder at West Egg Manor",
        "subtitle": "A Great Gatsby-inspired murder mystery for 26 players",
        "active": true,
        "currentRound": 2,
        "characters": [ { "id": "c1", "role": "The Heiress", "isMastermind": true } ],
        "clues": [ { "id": "cc1", "text": "A torn photograph", "released": true } ]
      }
    }
    """;

    private static TripData LoadV18WestEgg() =>
        JsonSerializer.Deserialize<TripData>(V18WestEggDocument, TripJson.Options)!;

    [Fact]
    public void V27_gives_everybody_a_join_token()
    {
        var trip = LoadV18WestEgg();

        Assert.True(TripMigrations.Apply(trip));

        Assert.All(trip.Roster, p => Assert.Equal(10, p.JoinToken!.Length));
        Assert.Equal(2, trip.Roster.Select(p => p.JoinToken).Distinct().Count());
    }

    [Fact]
    public void V28_renames_the_game_everywhere_a_guest_would_read_it()
    {
        var trip = LoadV18WestEgg();

        TripMigrations.Apply(trip);

        // Three places, all of them seen before the night: the deco slide, the itinerary row, and
        // the games list. Missing one leaves the old name on somebody's phone.
        Assert.Equal("Murder at Braun Manor", trip.Slides[0].Caption);
        Assert.Equal("Murder Mystery - Murder at Braun Manor", trip.Itinerary[0].Items[0].Title);
        Assert.Equal("Murder Mystery - Murder at Braun Manor", trip.Games[0].Name);
    }

    [Fact]
    public void V29_throws_away_the_old_game_rather_than_converting_it()
    {
        var trip = LoadV18WestEgg();

        TripMigrations.Apply(trip);

        // West Egg was 26 characters and a mastermind; the first Braun Manor was a generator; this
        // one is a written story and a phase machine. Nothing survives two rewrites, and a document
        // half-way between any two of them would be worst exactly where being wrong matters most.
        Assert.Equal(MysteryPhase.Lobby, trip.Mystery.Phase);
        Assert.Empty(trip.Mystery.Play.Cast);
        Assert.Empty(trip.Mystery.Play.Trials);
        Assert.Empty(trip.Mystery.Story.Characters);
        Assert.Equal(TripMigrations.CurrentVersion, trip.SchemaVersion);
    }

    [Fact]
    public void V28_leaves_a_title_somebody_has_since_chosen_deliberately()
    {
        var trip = LoadV18WestEgg();
        trip.Slides[0].Caption = "Murder at the Manor (final name TBC)";

        TripMigrations.Apply(trip);

        Assert.Equal("Murder at the Manor (final name TBC)", trip.Slides[0].Caption);
    }

    [Fact]
    public void V27_and_V28_running_twice_change_nothing_the_second_time()
    {
        var trip = LoadV18WestEgg();
        TripMigrations.Apply(trip);

        Assert.False(TripMigrations.Apply(trip));
    }

    /// <summary>A v25 file, listing the game the way every copy written before this one did.</summary>
    private static TripData NewlywedStillListed() => new()
    {
        SchemaVersion = 25,
        Games =
        [
            new Game { Id = "jeopardy", Name = "Jeopardy" },
            new Game { Id = "newlywed", Name = "Newlywed Game", Scoring = "newlywed" },
            new Game { Id = "sketch", Name = "Police Sketch" }
        ],
        Itinerary =
        [
            new ItineraryDay
            {
                Id = "day-fri",
                Items =
                [
                    new ItineraryItem { Id = "item-f4", StartMinutes = 1320, DurationMinutes = 60, Title = "Jeopardy" },
                    new ItineraryItem { Id = "item-f5", StartMinutes = 1380, DurationMinutes = 45, Title = "Newlywed Game" },
                    new ItineraryItem { Id = "item-f6", StartMinutes = 1425, DurationMinutes = 45, Title = "Police Sketch (optional)" }
                ]
            }
        ]
    };

    [Fact]
    public void An_already_current_document_is_left_alone()
    {
        var trip = LoadV18WestEgg();
        TripMigrations.Apply(trip);
        var tokens = trip.Roster.ToDictionary(p => p.Id, p => p.JoinToken);

        TripMigrations.Apply(trip);

        // Tokens especially: reissuing one is a name tag that has stopped working.
        Assert.All(trip.Roster, p => Assert.Equal(tokens[p.Id], p.JoinToken));
        Assert.Equal(TripMigrations.CurrentVersion, trip.SchemaVersion);
    }

    // ---- v32: the story is written ------------------------------------------------------------

    /// <summary>
    /// A trip that seeded the unwritten story gets the written one. StoryLoader.SeedInto cannot do
    /// this — it is guarded on Seeded, and rightly so — which is the whole reason for a step.
    /// </summary>
    [Fact]
    public void V32_writes_the_story_into_a_trip_that_seeded_the_unwritten_one()
    {
        var trip = new TripData { SchemaVersion = 31 };
        trip.Mystery.Story = new MysteryStory
        {
            Seeded = true,
            Characters = [new MysteryCharacter { Id = "wilhelm", Name = "Wilhelm Shepard", Backstory = "........" }]
        };

        Assert.True(TripMigrations.Apply(trip));
        Assert.Equal(TripMigrations.CurrentVersion, trip.SchemaVersion);

        var wilhelm = trip.Mystery.Story.Character("wilhelm");
        Assert.NotNull(wilhelm);
        Assert.False(MysteryText.IsPlaceholder(wilhelm!.Backstory));
        Assert.Equal(25, trip.Mystery.Story.Characters.Count);
    }

    /// <summary>
    /// The argument for replacing the story wholesale rather than filling its gaps: the written
    /// copy is the same cast under the same ids, and Play refers to all of it by id alone. An
    /// evening already underway keeps its cast, its tokens, its scans and its votes, and gains only
    /// the words. If this ever fails, the replace has become destructive and needs to become a
    /// merge.
    /// </summary>
    [Fact]
    public void V32_leaves_an_evening_already_underway_alone()
    {
        var trip = new TripData { SchemaVersion = 31 };
        trip.Mystery.Phase = MysteryPhase.Investigation;
        trip.Mystery.Story = new MysteryStory { Seeded = true };
        trip.Mystery.Play.PartyCode = "MANOR";
        trip.Mystery.Play.Cast.Add(new MysteryCastMember
        {
            CharacterId = "carla",
            PersonId = "p-7",
            BadgeToken = "badge-abcdef"
        });
        trip.Mystery.Play.ClueStates.Add(new MysteryClueState { ClueId = "clue-lawn", Token = "clue-tok-123" });

        TripMigrations.Apply(trip);

        Assert.Equal(MysteryPhase.Investigation, trip.Mystery.Phase);
        Assert.Equal("MANOR", trip.Mystery.Play.PartyCode);
        Assert.Equal("badge-abcdef", Assert.Single(trip.Mystery.Play.Cast).BadgeToken);
        Assert.Equal("clue-tok-123", Assert.Single(trip.Mystery.Play.ClueStates).Token);

        // And the ids the play state points at are all still there to point at.
        Assert.NotNull(trip.Mystery.Story.Character("carla"));
        Assert.NotNull(trip.Mystery.Story.Clue("clue-lawn"));
    }

    /// <summary>
    /// A trip that has never seeded is left empty. StoryLoader.SeedInto brings the written copy the
    /// first time somebody creates an evening; writing a story into a trip that asked for none
    /// would be a game nobody started.
    /// </summary>
    [Fact]
    public void V32_does_not_start_a_story_nobody_asked_for()
    {
        var trip = new TripData { SchemaVersion = 31 };

        TripMigrations.Apply(trip);

        Assert.False(trip.Mystery.Story.Seeded);
        Assert.Empty(trip.Mystery.Story.Characters);
    }

    [Fact]
    public void V32_running_twice_changes_nothing_the_second_time()
    {
        var trip = new TripData { SchemaVersion = 31 };
        trip.Mystery.Story = new MysteryStory { Seeded = true };

        TripMigrations.Apply(trip);

        Assert.False(TripMigrations.Apply(trip));
    }

}
