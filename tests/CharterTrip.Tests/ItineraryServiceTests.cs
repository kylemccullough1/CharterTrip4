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

    // ----------------------------------------------------------------- time

    [Fact]
    public void SetStart_moves_the_item_and_resorts_the_day()
    {
        var t = Trip();
        ItineraryService.SetStart(t, "a", 9 * 60);   // dinner at 9am, before check-in

        Assert.Equal(9 * 60, ItineraryService.Locate(t, "a").Item!.StartMinutes);
        Assert.Equal(["a", "b", "c"], Ids(t, "fri"));
    }

    [Fact]
    public void NudgeStart_shifts_by_the_delta_and_snaps()
    {
        var t = Trip();
        ItineraryService.NudgeStart(t, "b", 15);
        Assert.Equal(FourPm + 15, ItineraryService.Locate(t, "b").Item!.StartMinutes);

        // An off-grid start gets pulled back onto the 15-minute grid.
        ItineraryService.SetStart(t, "b", FourPm + 7);
        ItineraryService.NudgeStart(t, "b", 15);
        Assert.Equal(FourPm + 15, ItineraryService.Locate(t, "b").Item!.StartMinutes);
    }

    [Fact]
    public void NudgeStart_ignores_unscheduled_items()
    {
        var t = Trip();
        ItineraryService.Unschedule(t, "a");
        ItineraryService.NudgeStart(t, "a", 30);

        Assert.Null(ItineraryService.Locate(t, "a").Item!.StartMinutes);
    }

    [Fact]
    public void Start_is_clamped_to_the_planner_window()
    {
        var t = Trip();

        ItineraryService.SetStart(t, "b", -500);
        Assert.Equal(ItineraryService.EarliestStart, ItineraryService.Locate(t, "b").Item!.StartMinutes);

        ItineraryService.SetStart(t, "b", 99_999);
        Assert.Equal(ItineraryService.LatestStart, ItineraryService.Locate(t, "b").Item!.StartMinutes);
    }

    [Fact]
    public void Duration_is_clamped_to_sane_bounds()
    {
        var t = Trip();

        ItineraryService.SetDuration(t, "a", 1);
        Assert.Equal(ItineraryService.MinDuration, ItineraryService.Locate(t, "a").Item!.DurationMinutes);

        ItineraryService.SetDuration(t, "a", 10_000);
        Assert.Equal(ItineraryService.MaxDuration, ItineraryService.Locate(t, "a").Item!.DurationMinutes);
    }

    [Fact]
    public void NudgeDuration_grows_and_shrinks_the_block()
    {
        var t = Trip();
        ItineraryService.NudgeDuration(t, "a", 30);
        Assert.Equal(120, ItineraryService.Locate(t, "a").Item!.DurationMinutes);

        ItineraryService.NudgeDuration(t, "a", -90);
        Assert.Equal(30, ItineraryService.Locate(t, "a").Item!.DurationMinutes);
    }

    // ----------------------------------------------------------------- move

    [Fact]
    public void MoveToDay_keeps_the_time_when_none_is_given()
    {
        var t = Trip();
        ItineraryService.MoveToDay(t, "a", "sat");

        var (day, item) = ItineraryService.Locate(t, "a");
        Assert.Equal("sat", day!.Id);
        Assert.Equal(EightPm, item!.StartMinutes);
    }

    [Fact]
    public void MoveToDay_reschedules_when_a_time_is_given()
    {
        var t = Trip();
        ItineraryService.MoveToDay(t, "a", "sat", 11 * 60);

        var (day, item) = ItineraryService.Locate(t, "a");
        Assert.Equal("sat", day!.Id);
        Assert.Equal(11 * 60, item!.StartMinutes);
        Assert.Equal(["d", "a"], Ids(t, "sat"));   // sorted into place
    }

    [Fact]
    public void MoveToAdjacentDay_stops_at_the_ends()
    {
        var t = Trip();
        ItineraryService.MoveToAdjacentDay(t, "d", +1);    // Saturday is last
        Assert.Equal("sat", ItineraryService.Locate(t, "d").Day!.Id);

        ItineraryService.MoveToAdjacentDay(t, "b", -1);    // Friday is first
        Assert.Equal("fri", ItineraryService.Locate(t, "b").Day!.Id);
    }

    // ---------------------------------------------------------- scheduling

    [Fact]
    public void Unschedule_sends_an_item_to_the_tray_and_keeps_its_details()
    {
        var t = Trip();
        ItineraryService.Unschedule(t, "a");

        var item = ItineraryService.Locate(t, "a").Item!;
        Assert.Null(item.StartMinutes);
        Assert.False(item.IsScheduled);
        Assert.Equal("Dinner", item.Title);
    }

    [Fact]
    public void SortDay_sinks_unscheduled_items_to_the_bottom()
    {
        var t = Trip();
        ItineraryService.Unschedule(t, "b");
        ItineraryService.SortDayByTime(t, "fri");

        Assert.Equal(["a", "c", "b"], Ids(t, "fri"));
    }

    [Fact]
    public void Midnight_sorts_to_the_end_of_the_night_not_the_start()
    {
        var t = Trip();
        ItineraryService.SortDayByTime(t, "fri");

        Assert.Equal(["b", "a", "c"], Ids(t, "fri"));
    }

    [Fact]
    public void AddItem_can_land_at_a_specific_time()
    {
        var t = Trip();
        var added = ItineraryService.AddItem(t, "fri", 18 * 60);

        Assert.NotNull(added);
        Assert.Equal(18 * 60, added!.StartMinutes);
        Assert.Equal(["b", added.Id, "a", "c"], Ids(t, "fri"));
    }

    [Fact]
    public void AddItem_without_a_time_goes_to_the_tray()
    {
        var t = Trip();
        var added = ItineraryService.AddItem(t, "fri");

        Assert.NotNull(added);
        Assert.Null(added!.StartMinutes);
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
