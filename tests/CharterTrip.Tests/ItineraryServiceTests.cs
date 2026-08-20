using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class ItineraryServiceTests
{
    private static TripData Trip() => new()
    {
        Itinerary =
        [
            new ItineraryDay { Id = "fri", Day = "Friday", Items =
            [
                new ItineraryItem { Id = "a", Time = "8:00 PM", Title = "Dinner" },
                new ItineraryItem { Id = "b", Time = "4:00 PM", Title = "Check-in" },
                new ItineraryItem { Id = "c", Time = "12:00 AM", Title = "Nightcap" }
            ]},
            new ItineraryDay { Id = "sat", Day = "Saturday", Items =
            [
                new ItineraryItem { Id = "d", Time = "10:00 AM", Title = "Breakfast" }
            ]}
        ]
    };

    private static List<string> Order(TripData t, string dayId) =>
        t.Itinerary.Single(d => d.Id == dayId).Items.Select(i => i.Id).ToList();

    [Fact]
    public void Nudge_moves_an_item_within_its_day()
    {
        var t = Trip();
        ItineraryService.Nudge(t, "b", -1);
        Assert.Equal(["b", "a", "c"], Order(t, "fri"));
    }

    [Fact]
    public void Nudge_at_the_edges_does_nothing()
    {
        var t = Trip();
        ItineraryService.Nudge(t, "a", -1);   // already first
        ItineraryService.Nudge(t, "c", +1);   // already last
        Assert.Equal(["a", "b", "c"], Order(t, "fri"));
    }

    [Fact]
    public void MoveToAdjacentDay_appends_to_the_next_day()
    {
        var t = Trip();
        ItineraryService.MoveToAdjacentDay(t, "a", +1);

        Assert.Equal(["b", "c"], Order(t, "fri"));
        Assert.Equal(["d", "a"], Order(t, "sat"));
    }

    [Fact]
    public void MoveToAdjacentDay_past_the_last_day_does_nothing()
    {
        var t = Trip();
        ItineraryService.MoveToAdjacentDay(t, "d", +1);
        Assert.Equal(["d"], Order(t, "sat"));
    }

    [Fact]
    public void MoveItem_drops_at_an_index_in_another_day()
    {
        var t = Trip();
        ItineraryService.MoveItem(t, "a", "sat", 0);

        Assert.Equal(["b", "c"], Order(t, "fri"));
        Assert.Equal(["a", "d"], Order(t, "sat"));
    }

    [Fact]
    public void MoveItem_clamps_an_out_of_range_index()
    {
        var t = Trip();
        ItineraryService.MoveItem(t, "a", "sat", 99);
        Assert.Equal(["d", "a"], Order(t, "sat"));
    }

    [Fact]
    public void MoveItem_can_reorder_within_the_same_day()
    {
        var t = Trip();
        ItineraryService.MoveItem(t, "c", "fri", 0);
        Assert.Equal(["c", "a", "b"], Order(t, "fri"));
    }

    [Fact]
    public void SortDayByTime_puts_midnight_last()
    {
        var t = Trip();
        ItineraryService.SortDayByTime(t, "fri");
        Assert.Equal(["b", "a", "c"], Order(t, "fri"));   // 4pm, 8pm, 12am
    }

    [Fact]
    public void SortDayByTime_keeps_unparseable_times_at_the_bottom_in_order()
    {
        var t = Trip();
        var day = t.Itinerary[0];
        day.Items.Add(new ItineraryItem { Id = "x", Time = "TBD", Title = "Something" });
        day.Items.Add(new ItineraryItem { Id = "y", Time = "after dinner", Title = "Else" });

        ItineraryService.SortDayByTime(t, "fri");

        Assert.Equal(["b", "a", "c", "x", "y"], Order(t, "fri"));
    }

    [Fact]
    public void Add_and_remove_items()
    {
        var t = Trip();
        var added = ItineraryService.AddItem(t, "sat");

        Assert.NotNull(added);
        Assert.Equal(2, t.Itinerary[1].Items.Count);

        ItineraryService.RemoveItem(t, added!.Id);
        Assert.Equal(["d"], Order(t, "sat"));
    }

    [Fact]
    public void Add_and_remove_days()
    {
        var t = Trip();
        var day = ItineraryService.AddDay(t, "Monday", "Aug 31");

        Assert.Equal(3, t.Itinerary.Count);

        ItineraryService.RemoveDay(t, day.Id);
        Assert.Equal(2, t.Itinerary.Count);
    }

    [Fact]
    public void Operations_on_unknown_ids_are_no_ops()
    {
        var t = Trip();
        ItineraryService.Nudge(t, "nope", 1);
        ItineraryService.MoveItem(t, "nope", "sat", 0);
        ItineraryService.MoveToAdjacentDay(t, "nope", 1);
        ItineraryService.SortDayByTime(t, "nope");
        ItineraryService.RemoveItem(t, "nope");

        Assert.Equal(["a", "b", "c"], Order(t, "fri"));
        Assert.Equal(["d"], Order(t, "sat"));
    }
}
