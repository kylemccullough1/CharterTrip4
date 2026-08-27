using CharterTrip.Core.Models;
using CharterTrip.Infrastructure.Seed;
using CharterTrip.Web.Auth;

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
        Assert.Equal(25, Seed.Roster.Count);

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

        foreach (var category in Seed.Jeopardy.Categories)
            Assert.Equal([5, 10, 15, 20, 25], category.Clues.Select(c => c.Value));
    }

    [Fact]
    public void Every_jeopardy_clue_has_content_a_response_and_a_unique_id()
    {
        var clues = Seed.Jeopardy.Categories.SelectMany(c => c.Clues).ToList();

        Assert.Equal(25, clues.Count);
        Assert.All(clues, c => Assert.False(c.IsEmpty, $"{c.Id} has nothing to show"));
        Assert.All(clues, c => Assert.False(string.IsNullOrWhiteSpace(c.Response), $"{c.Id} has no answer"));
        Assert.Equal(25, clues.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Final_jeopardy_is_set_and_worth_thirty()
    {
        Assert.Equal(30, Seed.Jeopardy.Final.Value);
        Assert.False(string.IsNullOrWhiteSpace(Seed.Jeopardy.Final.Clue));
    }

    [Fact]
    public void Jeopardy_carries_the_two_image_clues()
    {
        var clues = Seed.Jeopardy.Categories.SelectMany(c => c.Clues).ToList();
        Assert.Equal(2, clues.Count(c => !string.IsNullOrWhiteSpace(c.ClueImage) || !string.IsNullOrWhiteSpace(c.ResponseImage)));
    }

    [Fact]
    public void The_seed_carries_no_dealt_mystery()
    {
        // The 21 Braun Manor characters live in the embedded script, not here, and the cast is
        // generated at deal time from a seed. A trip.seed.json carrying a deal would ship a
        // guilty list in git.
        Assert.Null(Seed.Mystery.Deal);
        Assert.False(Seed.Mystery.Active);
        Assert.Empty(Seed.Mystery.Clues);
        Assert.Empty(Seed.Mystery.Trials);
        Assert.Equal(-1, Seed.Mystery.CurrentRoundIndex);
    }

    [Fact]
    public void The_seed_carries_no_join_tokens()
    {
        // A join token is somebody's identity for the weekend. Twenty-five of them committed to a
        // repository is twenty-five accounts anybody who can read it can use. SeedPreparation
        // strips them on the way out; this is what notices if that stops happening.
        Assert.All(Seed.Roster, p => Assert.True(string.IsNullOrEmpty(p.JoinToken)));
    }

    [Fact]
    public void Games_are_populated()
    {
        Assert.Equal(6, Seed.Games.Count);
        Assert.All(Seed.Games, g => Assert.NotEmpty(g.Rules));
    }

    /// <summary>
    /// The menu names games by id and the ids live in the seed, so dropping a game leaves a menu
    /// entry pointing at nothing — /games/{id} falls through to the page's "Unknown game" card
    /// rather than failing anywhere a build would notice. Murder Mystery is the one entry with a
    /// page of its own rather than a seeded game behind it.
    /// </summary>
    [Fact]
    public void Every_game_in_the_menu_is_a_game_that_exists()
    {
        var known = Seed.Games.Select(g => g.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        known.Add("mystery");

        var linked = GameSlugsInTheMenu();

        Assert.NotEmpty(linked);
        Assert.All(linked, slug => Assert.True(
            known.Contains(slug),
            $"The menu links to /games/{slug}, but no seeded game and no page answers to it."));
    }

    /// <summary>And the other way about: a game with no way into it is a game nobody plays.</summary>
    [Fact]
    public void Every_seeded_game_is_reachable_from_the_menu()
    {
        var linked = GameSlugsInTheMenu().ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(Seed.Games, g => Assert.True(
            linked.Contains(g.Id), $"{g.Name} is in the seed but nowhere in the menu."));
    }

    private static List<string> GameSlugsInTheMenu() =>
        NavTree.All
            .Single(e => e.Label == "Games").Children!
            .Select(c => c.Href)
            .Where(h => h.StartsWith("/games/", StringComparison.OrdinalIgnoreCase))
            .Select(h => h["/games/".Length..])
            .ToList();

    [Fact]
    public void Nothing_has_a_blank_id()
    {
        Assert.All(Seed.Teams, t => Assert.False(string.IsNullOrWhiteSpace(t.Id)));
        Assert.All(Seed.Roster, p => Assert.False(string.IsNullOrWhiteSpace(p.Id)));
        Assert.All(Seed.Games, g => Assert.False(string.IsNullOrWhiteSpace(g.Id)));
        Assert.All(Seed.Itinerary, d => Assert.False(string.IsNullOrWhiteSpace(d.Id)));
    }
}
