using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
using CharterTrip.Core.Words;
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
    public void The_bee_ships_with_a_difficulty_rather_than_a_word_list()
    {
        var bee = Seed.SpellingBee;

        // Words are drawn as turns come up, so shipping any would only mean shipping a stale
        // hand somebody had to remember to clear.
        Assert.Empty(bee.Words);

        Assert.True(WordBank.IsTier(bee.DifficultyKey), $"'{bee.DifficultyKey}' is not a tier");
        Assert.Equal(bee.DifficultyKey, bee.Game.DifficultyKey);
        Assert.True(bee.PointsPerWord > 0);
        Assert.Equal(-1, bee.Game.RuleSlide);
    }

    /// <summary>
    /// The dress rehearsal: the real roster, the real teams, real drawn words, played to a
    /// finish. Everything else about the bee is tested on a four-person fixture, which proves the
    /// rules but not that they survive contact with twenty-five people across four teams — and
    /// the two ways this could go wrong on the night are that it never ends, or that it runs out
    /// of words before it does.
    /// </summary>
    [Fact]
    public void A_full_bee_on_the_real_roster_finishes_without_running_out_of_words()
    {
        var trip = SeedLoader.Load();
        var now = new DateTimeOffset(2026, 8, 29, 11, 30, 0, TimeSpan.Zero);

        foreach (var person in trip.Roster) SpellingBeeService.SetReady(trip, person.Id, true);
        SpellingBeeService.Start(trip, new Random(7));

        Assert.Equal(BeePhase.Spelling, trip.SpellingBee.Game.Phase);
        Assert.Equal(trip.Roster.Count, trip.SpellingBee.Game.Order.Count);

        // Everybody misses until one is left. The cap is a deadlock detector, not a limit.
        var turns = 0;
        while (trip.SpellingBee.Game.Phase != BeePhase.Finished && turns++ < 500)
        {
            if (SpellingBeeService.Survivors(trip).Count == 1)
                SpellingBeeService.JudgeCorrect(trip, now);
            else
                SpellingBeeService.JudgeWrong(trip);

            SpellingBeeService.Continue(trip, new Random(5));
        }

        Assert.Equal(BeePhase.Finished, trip.SpellingBee.Game.Phase);

        var winner = SpellingBeeService.Winner(trip);
        Assert.NotNull(winner);
        Assert.Contains(trip.Teams, t => t.Id == winner!.TeamId);

        // Every word the bee read was a different word, all the way to the end.
        var words = trip.SpellingBee.Words;
        Assert.Equal(words.Count, words.Select(w => w.Word.ToLowerInvariant()).Distinct().Count());
        Assert.All(words, w => Assert.False(w.IsEmpty, $"{w.Id} has no word"));
    }


    /// <summary>
    /// The same rehearsal, but the last one standing fumbles it once before winning — which is
    /// the only path that reaches the revival rule, and the one the whole endgame turns on.
    ///
    /// Two ways this goes wrong on the night and neither shows up on a four-person fixture: the
    /// refill never terminates because every miss brings four people back, or it eats the deck
    /// getting there.
    /// </summary>
    [Fact]
    public void A_revival_on_the_real_roster_refills_the_field_and_still_ends()
    {
        var trip = SeedLoader.Load();
        var now = new DateTimeOffset(2026, 8, 29, 11, 30, 0, TimeSpan.Zero);

        foreach (var person in trip.Roster) SpellingBeeService.SetReady(trip, person.Id, true);
        SpellingBeeService.Start(trip, new Random(23));

        var game = trip.SpellingBee.Game;
        var fumbled = false;
        var revivedCount = 0;

        var turns = 0;
        while (game.Phase != BeePhase.Finished && turns++ < 500)
        {
            if (SpellingBeeService.Survivors(trip).Count == 1 && fumbled)
            {
                SpellingBeeService.JudgeCorrect(trip, now);
            }
            else
            {
                var survivor = SpellingBeeService.Person(trip, game.CurrentPersonId)!;
                var wasLast = SpellingBeeService.Survivors(trip).Count == 1;

                SpellingBeeService.JudgeWrong(trip);

                if (wasLast)
                {
                    // The refill happened, it did not put the speller out, and it drew from
                    // every team except their own.
                    fumbled = true;
                    revivedCount = game.JustRevived.Count;

                    Assert.NotEmpty(game.JustRevived);
                    Assert.False(SpellingBeeService.IsOut(trip, survivor.Id));
                    Assert.All(game.JustRevived, id =>
                        Assert.NotEqual(survivor.TeamId, SpellingBeeService.Person(trip, id)!.TeamId));
                }
            }

            SpellingBeeService.Continue(trip, new Random(5));
        }

        Assert.True(fumbled, "the last one standing never missed, so no revival was exercised");
        Assert.Equal(trip.Teams.Count - 1, revivedCount);   // every team but the survivor's own

        Assert.Equal(BeePhase.Finished, game.Phase);
        Assert.NotNull(SpellingBeeService.Winner(trip));

        var words = trip.SpellingBee.Words;
        Assert.Equal(words.Count, words.Select(w => w.Word.ToLowerInvariant()).Distinct().Count());
    }

    /// <summary>
    /// The row and whoever is at the microphone are two records of the same fact, so they must
    /// never disagree. Checked after every operation of a long game rather than at the end,
    /// because a rotation that drifts one turn shows up as the wrong person being called much
    /// later, when it is far too late to work out why.
    /// </summary>
    [Fact]
    public void The_speller_is_always_somebody_still_in_the_row()
    {
        var trip = SeedLoader.Load();
        var now = new DateTimeOffset(2026, 8, 29, 11, 30, 0, TimeSpan.Zero);

        foreach (var person in trip.Roster) SpellingBeeService.SetReady(trip, person.Id, true);
        SpellingBeeService.Start(trip, new Random(11));

        void Check(string after)
        {
            var game = trip.SpellingBee.Game;
            if (game.CurrentPersonId is not { } id) return;
            if (game.Phase == BeePhase.Finished) return;

            Assert.True(game.Order.Contains(id), $"after {after}: {id} is not in the row");
            Assert.False(game.Eliminated.Contains(id) && game.Phase == BeePhase.Spelling,
                $"after {after}: {id} is spelling but is out");
        }

        Check("start");

        var turns = 0;
        while (trip.SpellingBee.Game.Phase != BeePhase.Finished && turns++ < 500)
        {
            if (turns % 7 == 0)
            {
                SpellingBeeService.SkipWord(trip, new Random(5));
                Check("a skip");
                continue;
            }

            if (SpellingBeeService.Survivors(trip).Count == 1) SpellingBeeService.JudgeCorrect(trip, now);
            else SpellingBeeService.JudgeWrong(trip);
            Check("a judgement");

            SpellingBeeService.Continue(trip, new Random(5));
            Check("moving on");
        }

        Assert.Equal(BeePhase.Finished, trip.SpellingBee.Game.Phase);
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
