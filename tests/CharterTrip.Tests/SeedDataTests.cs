using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
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
    public void There_is_a_mystery_role_for_everyone_going()
    {
        // West Egg Manor is written for 26 and the roster is 25, so a role goes spare. What
        // matters is that nobody is left without one — the surplus is the host's to trim.
        Assert.True(Seed.Mystery.Characters.Count >= Seed.Roster.Count,
            $"{Seed.Roster.Count} people but only {Seed.Mystery.Characters.Count} roles");

        Assert.Equal(5, Seed.Mystery.Characters.Count(c => c.IsConspirator));
        Assert.Equal(1, Seed.Mystery.Characters.Count(c => c.IsMastermind));
    }

    [Fact]
    public void Spelling_bee_has_a_word_list_with_no_repeats()
    {
        var words = Seed.SpellingBee.Words;

        Assert.NotEmpty(words);
        Assert.All(words, w => Assert.False(w.IsEmpty, $"{w.Id} has no word"));
        Assert.All(words, w => Assert.False(string.IsNullOrWhiteSpace(w.Hint), $"{w.Word} has no hint to read"));

        // A word coming round twice is the one mistake a room notices instantly.
        Assert.Equal(words.Count, words.Select(w => w.Word.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(words.Count, words.Select(w => w.Id).Distinct().Count());
    }

    /// <summary>
    /// The dress rehearsal: the real roster, the real teams, the real word list, played to a
    /// finish. Everything else about the bee is tested on a four-person fixture, which proves the
    /// rules but not that they survive contact with twenty-five people across four teams — and
    /// the two ways this could go wrong on the night are that it never ends, or that it runs out
    /// of words before it does.
    /// </summary>
    [Fact]
    public void A_full_bee_on_the_real_roster_finishes_without_running_out_of_words()
    {
        var trip = SeedLoader.Load();
        SpellingBeeService.Start(trip);

        // Everybody misses until one is left, then the survivor spells their way to the win.
        // The cap is a deadlock detector, not a limit: a bee this size should end well inside it.
        var turns = 0;
        while (trip.SpellingBee.Game.Phase != BeePhase.Finished && turns++ < 500)
        {
            if (trip.SpellingBee.Game.Survivors.Count == 1)
                SpellingBeeService.JudgeCorrect(trip);
            else
                SpellingBeeService.JudgeWrong(trip);

            SpellingBeeService.Continue(trip);
        }

        Assert.Equal(BeePhase.Finished, trip.SpellingBee.Game.Phase);

        var winner = SpellingBeeService.Winner(trip);
        Assert.NotNull(winner);
        Assert.Contains(trip.Teams, t => t.Id == winner!.TeamId);

        // 25 people means 24 eliminations and a winning word — the list has to outlast that.
        Assert.True(SpellingBeeService.WordsRemaining(trip) > 0,
            $"the bee used every word in the list with {turns} turns played");

        var entry = Assert.Single(trip.Scores, s => s.GameId == SpellingBeeService.GameId);
        Assert.Equal(winner!.TeamId, entry.TeamId);
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
