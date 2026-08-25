using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

/// <summary>
/// What a guest is allowed to know about the games. The committee's own view goes through a
/// different branch entirely and is not screened at all.
/// </summary>
public class GuestScheduleTests
{
    private static ItineraryItem Item(string title, ItineraryTag tag, int startMinutes, int duration = 60) =>
        new()
        {
            Id = $"item-{title.ToLowerInvariant().Replace(' ', '-')}",
            Title = title,
            Tag = tag,
            StartMinutes = startMinutes,
            DurationMinutes = duration
        };

    [Fact]
    public void Adjacent_games_become_one_block()
    {
        var screened = GuestSchedule.Screen(
        [
            Item("Jeopardy", ItineraryTag.Game, 18 * 60, 60),
            Item("Spelling Bee", ItineraryTag.Game, 19 * 60, 60),
            Item("Police Sketch", ItineraryTag.Game, 20 * 60, 30)
        ]);

        var block = Assert.Single(screened);
        Assert.Equal("Games", block.Title);
        Assert.Equal(18 * 60, block.StartMinutes);
        Assert.Equal(150, block.DurationMinutes);   // 6:00 through 8:30
    }

    [Fact]
    public void A_single_game_is_still_anonymised()
    {
        // The one-game case is exactly the one that would give the surprise away.
        var screened = GuestSchedule.Screen([Item("Jeopardy", ItineraryTag.Game, 20 * 60)]);

        Assert.Equal("Games", Assert.Single(screened).Title);
    }

    [Fact]
    public void Anything_between_two_games_keeps_them_apart()
    {
        var screened = GuestSchedule.Screen(
        [
            Item("Jeopardy", ItineraryTag.Game, 16 * 60),
            Item("Dinner", ItineraryTag.Food, 17 * 60),
            Item("Spelling Bee", ItineraryTag.Game, 18 * 60)
        ]);

        Assert.Equal(["Games", "Dinner", "Games"], screened.Select(i => i.Title));
        Assert.Equal([60, 60, 60], screened.Select(i => i.DurationMinutes));
    }

    [Fact]
    public void Everything_else_is_left_exactly_as_it_was()
    {
        var dinner = Item("Dinner", ItineraryTag.Food, 18 * 60);
        var screened = GuestSchedule.Screen([dinner]);

        Assert.Same(dinner, Assert.Single(screened));
    }

    [Fact]
    public void Screening_never_touches_the_real_items()
    {
        var jeopardy = Item("Jeopardy", ItineraryTag.Game, 18 * 60);
        var bee = Item("Spelling Bee", ItineraryTag.Game, 19 * 60);

        GuestSchedule.Screen([jeopardy, bee]);

        Assert.Equal("Jeopardy", jeopardy.Title);
        Assert.Equal("Spelling Bee", bee.Title);
        Assert.Equal(60, jeopardy.DurationMinutes);
    }

    [Fact]
    public void Adjacency_is_decided_by_the_clock_not_by_list_order()
    {
        // Handed back to front, these are still one run of games either side of nothing.
        var screened = GuestSchedule.Screen(
        [
            Item("Spelling Bee", ItineraryTag.Game, 19 * 60),
            Item("Jeopardy", ItineraryTag.Game, 18 * 60)
        ]);

        var block = Assert.Single(screened);
        Assert.Equal(18 * 60, block.StartMinutes);
        Assert.Equal(120, block.DurationMinutes);
    }

    [Fact]
    public void A_run_that_overlaps_itself_spans_the_latest_finish()
    {
        // Two games at once is a real thing on this trip — the block has to cover both.
        var screened = GuestSchedule.Screen(
        [
            Item("Beer Run", ItineraryTag.Game, 15 * 60, 180),
            Item("Relay Race", ItineraryTag.Game, 16 * 60, 30)
        ]);

        Assert.Equal(180, Assert.Single(screened).DurationMinutes);
    }

    [Fact]
    public void An_empty_day_screens_to_nothing()
    {
        Assert.Empty(GuestSchedule.Screen([]));
    }
}
