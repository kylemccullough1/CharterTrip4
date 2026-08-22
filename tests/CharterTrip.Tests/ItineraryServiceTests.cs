using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class ItineraryServiceTests
{
    private const int FourPm = 16 * 60;      // 960
    private const int EightPm = 20 * 60;     // 1200
    private const int Midnight = 24 * 60;    // 1440 — the tail of the night, not the start
    private const int TenAm = 10 * 60;       // 600

    private static TripData Trip() => new()
    {
        Itinerary =
        [
            new ItineraryDay
            {
                Id = "fri", Day = "Friday",
                Items =
                [
                    new ItineraryItem { Id = "a", StartMinutes = EightPm, DurationMinutes = 90, Title = "Dinner" },
                    new ItineraryItem { Id = "b", StartMinutes = FourPm, DurationMinutes = 60, Title = "Check-in" },
                    new ItineraryItem { Id = "c", StartMinutes = Midnight, DurationMinutes = 60, Title = "Nightcap" }
                ]
            },
            new ItineraryDay
            {
                Id = "sat", Day = "Saturday",
                Items = [new ItineraryItem { Id = "d", StartMinutes = TenAm, DurationMinutes = 60, Title = "Breakfast" }]
            }
        ]
    };

    private static List<string> Ids(TripData t, string dayId) =>
        ItineraryService.FindDay(t, dayId)!.Items.Select(i => i.Id).ToList();

    private static ItineraryItem Item(TripData t, string id) => ItineraryService.Locate(t, id).Item!;

    private static ItemEdit EditFor(TripData t, string id, string dayId,
        string? title = null, int? start = null, int? duration = null, ItineraryTag? tag = null, string? notes = null)
    {
        var item = Item(t, id);
        return new ItemEdit(id, item.Version, dayId,
            title ?? item.Title, notes ?? item.Notes, tag ?? item.Tag,
            start ?? item.StartMinutes, duration ?? item.DurationMinutes);
    }

    // ---------------------------------------------------------------- saving

    [Fact]
    public void ApplyEdit_commits_the_whole_form_at_once()
    {
        var t = Trip();
        var edit = EditFor(t, "b", "fri", title: "Arrive", start: 17 * 60, duration: 45, tag: ItineraryTag.Food);

        Assert.Equal(SaveOutcome.Saved, ItineraryService.ApplyEdit(t, edit));

        var item = Item(t, "b");
        Assert.Equal("Arrive", item.Title);
        Assert.Equal(17 * 60, item.StartMinutes);
        Assert.Equal(45, item.DurationMinutes);
        Assert.Equal(ItineraryTag.Food, item.Tag);
    }

    [Fact]
    public void ApplyEdit_can_move_the_item_to_another_day()
    {
        var t = Trip();
        Assert.Equal(SaveOutcome.Saved, ItineraryService.ApplyEdit(t, EditFor(t, "a", "sat")));

        Assert.Equal("sat", ItineraryService.Locate(t, "a").Day!.Id);
        Assert.Equal(["d", "a"], Ids(t, "sat"));
    }

    [Fact]
    public void ApplyEdit_bumps_the_version()
    {
        var t = Trip();
        var before = Item(t, "b").Version;

        ItineraryService.ApplyEdit(t, EditFor(t, "b", "fri", title: "Changed"));

        Assert.Equal(before + 1, Item(t, "b").Version);
    }

    [Fact]
    public void ApplyEdit_refuses_a_stale_edit_and_changes_nothing()
    {
        var t = Trip();
        var stale = EditFor(t, "b", "fri", title: "Mine");

        // Someone else saves first, moving the version on.
        ItineraryService.ApplyEdit(t, EditFor(t, "b", "fri", title: "Theirs"));

        Assert.Equal(SaveOutcome.Conflict, ItineraryService.ApplyEdit(t, stale));
        Assert.Equal("Theirs", Item(t, "b").Title);
    }

    [Fact]
    public void A_forced_edit_wins_over_a_newer_version()
    {
        var t = Trip();
        var stale = EditFor(t, "b", "fri", title: "Mine");
        ItineraryService.ApplyEdit(t, EditFor(t, "b", "fri", title: "Theirs"));

        Assert.Equal(SaveOutcome.Saved, ItineraryService.ApplyEdit(t, stale, force: true));
        Assert.Equal("Mine", Item(t, "b").Title);
    }

    [Fact]
    public void Editing_a_deleted_item_reports_it_rather_than_resurrecting_it()
    {
        var t = Trip();
        var edit = EditFor(t, "b", "fri", title: "Mine");
        ItineraryService.RemoveItem(t, "b");

        Assert.Equal(SaveOutcome.Missing, ItineraryService.ApplyEdit(t, edit));
        Assert.Equal(["a", "c"], Ids(t, "fri"));
    }

    [Fact]
    public void Two_people_editing_different_items_do_not_conflict()
    {
        var t = Trip();
        var mine = EditFor(t, "a", "fri", title: "Mine");
        var theirs = EditFor(t, "b", "fri", title: "Theirs");

        Assert.Equal(SaveOutcome.Saved, ItineraryService.ApplyEdit(t, theirs));
        Assert.Equal(SaveOutcome.Saved, ItineraryService.ApplyEdit(t, mine));
        Assert.Equal("Mine", Item(t, "a").Title);
        Assert.Equal("Theirs", Item(t, "b").Title);
    }

    [Fact]
    public void ApplyEdit_clamps_out_of_range_values()
    {
        var t = Trip();
        ItineraryService.ApplyEdit(t, EditFor(t, "b", "fri", start: -500, duration: 10_000));

        var item = Item(t, "b");
        Assert.Equal(ItineraryService.EarliestStart, item.StartMinutes);
        Assert.Equal(ItineraryService.MaxDuration, item.DurationMinutes);
    }

    // ------------------------------------------------------ direct manipulation

    [Fact]
    public void SetStart_moves_the_item_resorts_the_day_and_bumps_the_version()
    {
        var t = Trip();
        var before = Item(t, "a").Version;

        ItineraryService.SetStart(t, "a", 9 * 60);

        Assert.Equal(9 * 60, Item(t, "a").StartMinutes);
        Assert.Equal(["a", "b", "c"], Ids(t, "fri"));
        Assert.Equal(before + 1, Item(t, "a").Version);
    }

    [Fact]
    public void SetDuration_is_clamped_and_bumps_the_version()
    {
        var t = Trip();
        var before = Item(t, "a").Version;

        ItineraryService.SetDuration(t, "a", 1);
        Assert.Equal(ItineraryService.MinDuration, Item(t, "a").DurationMinutes);
        Assert.Equal(before + 1, Item(t, "a").Version);
    }

    [Fact]
    public void MoveToDay_keeps_the_time_when_none_is_given()
    {
        var t = Trip();
        ItineraryService.MoveToDay(t, "a", "sat");

        Assert.Equal("sat", ItineraryService.Locate(t, "a").Day!.Id);
        Assert.Equal(EightPm, Item(t, "a").StartMinutes);
    }

    [Fact]
    public void MoveToAdjacentDay_stops_at_the_ends()
    {
        var t = Trip();
        ItineraryService.MoveToAdjacentDay(t, "d", +1);
        Assert.Equal("sat", ItineraryService.Locate(t, "d").Day!.Id);

        ItineraryService.MoveToAdjacentDay(t, "b", -1);
        Assert.Equal("fri", ItineraryService.Locate(t, "b").Day!.Id);
    }

    // ----------------------------------------------------------------- swap

    [Fact]
    public void SwapSlots_exchanges_start_and_duration()
    {
        var t = Trip();
        ItineraryService.SwapSlots(t, "a", "b");

        Assert.Equal(FourPm, Item(t, "a").StartMinutes);
        Assert.Equal(60, Item(t, "a").DurationMinutes);
        Assert.Equal(EightPm, Item(t, "b").StartMinutes);
        Assert.Equal(90, Item(t, "b").DurationMinutes);
    }

    [Fact]
    public void SwapSlots_leaves_no_overlap_between_the_two()
    {
        var t = Trip();
        ItineraryService.SwapSlots(t, "a", "b");

        var first = Item(t, "a");
        var second = Item(t, "b");
        Assert.True(first.EndMinutes <= second.StartMinutes || second.EndMinutes <= first.StartMinutes);
    }

    [Fact]
    public void SwapSlots_works_across_days_and_bumps_both_versions()
    {
        var t = Trip();
        var (v1, v2) = (Item(t, "a").Version, Item(t, "d").Version);

        ItineraryService.SwapSlots(t, "a", "d");

        Assert.Equal("sat", ItineraryService.Locate(t, "a").Day!.Id);
        Assert.Equal(TenAm, Item(t, "a").StartMinutes);
        Assert.Equal("fri", ItineraryService.Locate(t, "d").Day!.Id);
        Assert.Equal(EightPm, Item(t, "d").StartMinutes);
        Assert.Equal(v1 + 1, Item(t, "a").Version);
        Assert.Equal(v2 + 1, Item(t, "d").Version);
    }

    [Fact]
    public void SwapSlots_with_itself_is_a_no_op()
    {
        var t = Trip();
        ItineraryService.SwapSlots(t, "a", "a");

        Assert.Equal(EightPm, Item(t, "a").StartMinutes);
        Assert.Equal(90, Item(t, "a").DurationMinutes);
    }

    // ------------------------------------------------------- adding, removing

    [Fact]
    public void Midnight_sorts_to_the_end_of_the_night_not_the_start()
    {
        var t = Trip();
        ItineraryService.SortDayByTime(t, "fri");

        Assert.Equal(["b", "a", "c"], Ids(t, "fri"));
    }

    [Fact]
    public void AddItem_lands_at_midday_by_default_and_is_always_scheduled()
    {
        var t = Trip();
        var added = ItineraryService.AddItem(t, "fri");

        Assert.NotNull(added);
        Assert.Equal(ItineraryItem.DefaultStartMinutes, added!.StartMinutes);
        Assert.Equal(1, added.Version);
    }

    [Fact]
    public void AddItem_can_land_at_a_specific_time()
    {
        var t = Trip();
        var added = ItineraryService.AddItem(t, "fri", 18 * 60);

        Assert.Equal(18 * 60, added!.StartMinutes);
        Assert.Equal(["b", added.Id, "a", "c"], Ids(t, "fri"));
    }

    [Fact]
    public void RemoveItem_and_RemoveDay_work()
    {
        var t = Trip();
        ItineraryService.RemoveItem(t, "a");
        Assert.Equal(["b", "c"], Ids(t, "fri"));

        ItineraryService.RemoveDay(t, "fri");
        Assert.Single(t.Itinerary);
    }
}
