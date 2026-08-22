using CharterTrip.Core.Models;
using CharterTrip.Infrastructure.Seed;

namespace CharterTrip.Tests;

/// <summary>
/// The seed is hand-maintained JSON and the models are C#. These tests are what stop the two
/// drifting apart — a renamed property shows up here instead of as an empty page at runtime.
/// </summary>
public class SeedDataTests
{
    private static readonly TripData Seed = SeedLoader.Load();

    [Fact]
    public void Seed_deserializes()
    {
        Assert.NotNull(Seed);
        Assert.Equal("Charter Trip", Seed.Trip.Name);
        Assert.Equal(2026, Seed.Trip.Year);
        Assert.Equal("Braun Manor", Seed.Trip.Venue);
    }

    [Fact]
    public void Everyone_is_on_the_trip_and_on_a_team()
    {
        Assert.Equal(26, Seed.Roster.Count);

        var teamIds = Seed.Teams.Select(t => t.Id).ToHashSet();
        Assert.All(Seed.Roster, p => Assert.Contains(p.TeamId, teamIds));
    }

    [Fact]
    public void Committee_are_the_four_admins()
    {
        var admins = Seed.Roster.Where(p => p.Role == TripRole.Admin).Select(p => p.Name).ToList();
        Assert.Equal(4, admins.Count);
    }

    [Fact]
    public void Itinerary_covers_all_three_days()
    {
        Assert.Equal(3, Seed.Itinerary.Count);
        Assert.Collection(Seed.Itinerary,
            d => Assert.Equal("Friday", d.Day),
            d => Assert.Equal("Saturday", d.Day),
            d => Assert.Equal("Sunday", d.Day));

        Assert.All(Seed.Itinerary, d => Assert.NotEmpty(d.Items));
    }

    [Fact]
    public void Itinerary_ids_are_unique()
    {
        var ids = Seed.Itinerary.SelectMany(d => d.Items).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Itinerary_tags_all_parsed_into_the_enum()
    {
        var items = Seed.Itinerary.SelectMany(d => d.Items).ToList();
        Assert.Contains(items, i => i.Tag == ItineraryTag.Food);
        Assert.Contains(items, i => i.Tag == ItineraryTag.Game);
        Assert.Contains(items, i => i.Tag == ItineraryTag.Logistics);
        Assert.Contains(items, i => i.Tag == ItineraryTag.FreeTime);
    }

    [Fact]
    public void Jeopardy_board_is_five_by_five()
    {
        Assert.Equal(5, Seed.Jeopardy.Categories.Count);
        Assert.Equal(5, Seed.Jeopardy.Values.Count);
        Assert.Equal(25, Seed.Jeopardy.Clues.Count);

        foreach (var category in Seed.Jeopardy.Categories)
            foreach (var value in Seed.Jeopardy.Values)
                Assert.Single(Seed.Jeopardy.Clues, c => c.Category == category && c.Value == value);
    }

    [Fact]
    public void Jeopardy_carries_the_two_image_clues()
    {
        Assert.Equal(2, Seed.Jeopardy.Clues.Count(c => !string.IsNullOrWhiteSpace(c.ImageUrl)));
    }

    [Fact]
    public void Mystery_has_26_roles_five_conspirators_and_one_mastermind()
    {
        Assert.Equal(26, Seed.Mystery.Characters.Count);
        Assert.Equal(5, Seed.Mystery.Characters.Count(c => c.IsConspirator));
        Assert.Single(Seed.Mystery.Characters, c => c.IsMastermind);

        // The mastermind must be one of the conspirators.
        var mastermind = Seed.Mystery.Characters.Single(c => c.IsMastermind);
        Assert.True(mastermind.IsConspirator);
    }

    [Fact]
    public void Mystery_roles_match_the_headcount()
    {
        Assert.Equal(Seed.Roster.Count, Seed.Mystery.Characters.Count);
    }

    [Fact]
    public void Games_are_populated()
    {
        Assert.Equal(8, Seed.Games.Count);
        Assert.All(Seed.Games, g => Assert.NotEmpty(g.Rules));
    }

    [Fact]
    public void Nothing_has_a_blank_id()
    {
        Assert.All(Seed.Teams, t => Assert.False(string.IsNullOrWhiteSpace(t.Id)));
        Assert.All(Seed.Roster, p => Assert.False(string.IsNullOrWhiteSpace(p.Id)));
        Assert.All(Seed.Games, g => Assert.False(string.IsNullOrWhiteSpace(g.Id)));
        Assert.All(Seed.Itinerary, d => Assert.False(string.IsNullOrWhiteSpace(d.Id)));
    }
}
