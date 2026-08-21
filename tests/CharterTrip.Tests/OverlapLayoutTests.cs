using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class OverlapLayoutTests
{
    private static ItineraryItem Item(string id, int start, int duration) =>
        new() { Id = id, StartMinutes = start, DurationMinutes = duration, Title = id };

    private static PositionedItem Find(IReadOnlyList<PositionedItem> layout, string id) =>
        layout.Single(p => p.Item.Id == id);

    [Fact]
    public void Items_that_do_not_overlap_all_take_the_full_width()
    {
        var layout = OverlapLayout.Arrange([Item("a", 600, 60), Item("b", 720, 60)]);

        Assert.All(layout, p =>
        {
            Assert.Equal(0, p.Column);
            Assert.Equal(1, p.ColumnCount);
        });
    }

    [Fact]
    public void Two_overlapping_items_sit_side_by_side()
    {
        var layout = OverlapLayout.Arrange([Item("a", 600, 120), Item("b", 630, 60)]);

        Assert.Equal(0, Find(layout, "a").Column);
        Assert.Equal(1, Find(layout, "b").Column);
        Assert.All(layout, p => Assert.Equal(2, p.ColumnCount));
    }

    [Fact]
    public void A_freed_column_is_reused_by_a_later_item()
    {
        // a: 10-12, b: 10:30-11, c: 11-12 — c can take b's column once b has finished.
        var layout = OverlapLayout.Arrange([Item("a", 600, 120), Item("b", 630, 30), Item("c", 660, 60)]);

        Assert.Equal(0, Find(layout, "a").Column);
        Assert.Equal(1, Find(layout, "b").Column);
        Assert.Equal(1, Find(layout, "c").Column);
        Assert.All(layout, p => Assert.Equal(2, p.ColumnCount));
    }

    [Fact]
    public void Separate_clusters_are_counted_independently()
    {
        // Morning pair overlaps; the afternoon item is alone and should be full width.
        var layout = OverlapLayout.Arrange(
            [Item("a", 600, 60), Item("b", 615, 30), Item("late", 900, 60)]);

        Assert.Equal(2, Find(layout, "a").ColumnCount);
        Assert.Equal(2, Find(layout, "b").ColumnCount);
        Assert.Equal(1, Find(layout, "late").ColumnCount);
    }

    [Fact]
    public void Three_way_overlaps_get_three_columns()
    {
        var layout = OverlapLayout.Arrange([Item("a", 600, 90), Item("b", 610, 90), Item("c", 620, 90)]);

        Assert.Equal([0, 1, 2], layout.OrderBy(p => p.Column).Select(p => p.Column));
        Assert.All(layout, p => Assert.Equal(3, p.ColumnCount));
    }

    [Fact]
    public void Touching_items_are_not_treated_as_overlapping()
    {
        // 10-11 and 11-12 share only an instant; they should stack, not split.
        var layout = OverlapLayout.Arrange([Item("a", 600, 60), Item("b", 660, 60)]);

        Assert.All(layout, p => Assert.Equal(1, p.ColumnCount));
    }

    [Fact]
    public void Unscheduled_items_are_left_out()
    {
        var tray = new ItineraryItem { Id = "tray", Title = "Someday", StartMinutes = null };
        var layout = OverlapLayout.Arrange([Item("a", 600, 60), tray]);

        Assert.Single(layout);
        Assert.Equal("a", layout[0].Item.Id);
    }
}
