using CharterTrip.Core.Models;
using CharterTrip.Core.Services;
using CharterTrip.Core.Words;

namespace CharterTrip.Tests;

public class SpellingBeeServiceTests
{
    /// <summary>
    /// One generator for the whole test, fixed so a shuffled running order is a decided one and
    /// the tests can name people.
    ///
    /// Shared rather than made fresh per call because the bee now draws a word every turn: a
    /// generator reseeded on each draw would take the same index out of every pool, which is a
    /// pattern no real run would produce.
    /// </summary>
    private readonly Random _rng = new(1234);

    private Random Rng() => _rng;

    private static readonly DateTimeOffset Now = new(2026, 8, 29, 11, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Two teams of deliberately different size. A has three, B has one — the bee no longer
    /// rotates by team, but points still land on teams, so uneven sides are what makes a
    /// scoring mistake visible.
    /// </summary>
    private TripData Trip(int aCount = 3, int bCount = 1)
    {
        var trip = new TripData
        {
            Teams =
            [
                new Team { Id = "a", Name = "Team A" },
                new Team { Id = "b", Name = "Team B" }
            ]
        };

        var aNames = new[] { "Ann", "Ben", "Cal", "Gus", "Hal" };
        var bNames = new[] { "Dee", "Eve", "Fay", "Ivy", "Joy" };

        for (var i = 0; i < aCount; i++)
            trip.Roster.Add(new RosterPerson { Id = aNames[i].ToLowerInvariant(), Name = aNames[i], TeamId = "a" });
        for (var i = 0; i < bCount; i++)
            trip.Roster.Add(new RosterPerson { Id = bNames[i].ToLowerInvariant(), Name = bNames[i], TeamId = "b" });

        return trip;
    }

    /// <summary>Everybody readies up and the bee starts. The usual opening.</summary>
    private TripData Started(int aCount = 3, int bCount = 1)
    {
        var trip = Trip(aCount, bCount);
        ReadyEveryone(trip);
        SpellingBeeService.Start(trip, Rng());
        return trip;
    }

    private void ReadyEveryone(TripData trip)
    {
        foreach (var person in trip.Roster)
            SpellingBeeService.SetReady(trip, person.Id, true);
    }

    private string? Speller(TripData trip) => trip.SpellingBee.Game.CurrentPersonId;

    /// <summary>Spell the current word right and move past the reveal.</summary>
    private void Correct(TripData trip)
    {
        SpellingBeeService.JudgeCorrect(trip, Now);
        SpellingBeeService.Continue(trip, Rng());
    }

    /// <summary>Miss the current word and move past the reveal.</summary>
    private void Wrong(TripData trip)
    {
        SpellingBeeService.JudgeWrong(trip);
        SpellingBeeService.Continue(trip, Rng());
    }

    /// <summary>The next <paramref name="turns"/> spellers, everyone getting their word right.</summary>
    private List<string> Order(TripData trip, int turns)
    {
        var seen = new List<string>();
        for (var i = 0; i < turns; i++)
        {
            seen.Add(Speller(trip)!);
            Correct(trip);
        }
        return seen;
    }

    // ------------------------------------------------------------------ setup

    [Fact]
    public void Nobody_plays_until_they_have_readied_up()
    {
        var trip = Trip();
        SpellingBeeService.Start(trip, Rng());

        Assert.Equal(BeePhase.NotStarted, trip.SpellingBee.Game.Phase);
        Assert.Empty(trip.SpellingBee.Game.Order);
    }

    [Fact]
    public void Only_the_people_who_readied_up_are_dealt_into_the_row()
    {
        var trip = Trip();
        SpellingBeeService.SetReady(trip, "ann", true);
        SpellingBeeService.SetReady(trip, "dee", true);

        SpellingBeeService.Start(trip, Rng());

        Assert.Equal(BeePhase.Spelling, trip.SpellingBee.Game.Phase);
        Assert.Equal(["ann", "dee"], trip.SpellingBee.Game.Order.Order());
    }

    [Fact]
    public void A_person_with_no_team_cannot_ready_up()
    {
        var trip = Trip();
        trip.Roster.Add(new RosterPerson { Id = "zed", Name = "Zed", TeamId = "" });

        SpellingBeeService.SetReady(trip, "zed", true);

        Assert.DoesNotContain("zed", trip.SpellingBee.Game.Ready);
    }

    [Fact]
    public void Changing_your_mind_takes_you_back_out_of_the_lobby()
    {
        var trip = Trip();
        SpellingBeeService.SetReady(trip, "ann", true);
        SpellingBeeService.SetReady(trip, "ann", false);

        Assert.Empty(trip.SpellingBee.Game.Ready);
        Assert.False(SpellingBeeService.CanStart(trip));
    }

    [Fact]
    public void Readying_up_does_nothing_once_the_bee_has_started()
    {
        var trip = Started();
        var before = trip.SpellingBee.Game.Ready.Count;

        trip.Roster.Add(new RosterPerson { Id = "late", Name = "Late", TeamId = "a" });
        SpellingBeeService.SetReady(trip, "late", true);

        Assert.Equal(before, trip.SpellingBee.Game.Ready.Count);
        Assert.DoesNotContain("late", trip.SpellingBee.Game.Order);
    }

    /// <summary>
    /// The room has just spent five minutes scanning those codes and readying up on them.
    /// Re-issuing at the moment of Start throws every phone in the building — the host's phone
    /// included, which is the one holding the words — back to the code box.
    /// </summary>
    [Fact]
    public void Starting_the_bee_leaves_the_join_codes_alone()
    {
        var trip = Trip();
        SpellingBeeService.EnsureCodes(trip, Rng());

        var guest = trip.SpellingBee.Game.GuestCode;
        var host = trip.SpellingBee.Game.HostCode;

        ReadyEveryone(trip);
        SpellingBeeService.Start(trip, Rng());

        Assert.Equal(guest, trip.SpellingBee.Game.GuestCode);
        Assert.Equal(host, trip.SpellingBee.Game.HostCode);
    }

    /// <summary>
    /// A reset is the opposite case: it means "forget this and start over", and a phone left open
    /// from the last run should not be able to walk into the next one.
    /// </summary>
    [Fact]
    public void Resetting_the_bee_issues_new_codes()
    {
        var trip = Started();
        var guest = trip.SpellingBee.Game.GuestCode;
        var host = trip.SpellingBee.Game.HostCode;

        SpellingBeeService.Reset(trip, new Random(999));

        Assert.NotEqual(guest, trip.SpellingBee.Game.GuestCode);
        Assert.NotEqual(host, trip.SpellingBee.Game.HostCode);
    }

    [Fact]
    public void Starting_deals_a_deck_and_calls_the_first_speller()
    {
        var trip = Started();

        Assert.Equal(BeePhase.Spelling, trip.SpellingBee.Game.Phase);
        Assert.NotNull(Speller(trip));
        Assert.NotEmpty(trip.SpellingBee.Words);
        Assert.NotNull(SpellingBeeService.CurrentWord(trip));
    }

    [Fact]
    public void The_two_join_codes_are_never_the_same()
    {
        var trip = Trip();
        SpellingBeeService.EnsureCodes(trip, Rng());

        var game = trip.SpellingBee.Game;
        Assert.NotEqual(game.GuestCode, game.HostCode);
        Assert.True(SpellingBeeService.IsGuestCode(trip, game.GuestCode.ToLowerInvariant()));
        Assert.True(SpellingBeeService.IsHostCode(trip, game.HostCode.ToLowerInvariant()));
        Assert.False(SpellingBeeService.IsHostCode(trip, game.GuestCode));
    }

    [Fact]
    public void Codes_avoid_characters_that_get_misread_across_a_room()
    {
        var trip = Trip();

        for (var i = 0; i < 200; i++)
        {
            trip.SpellingBee.Game.GuestCode = "";
            trip.SpellingBee.Game.HostCode = "";
            SpellingBeeService.EnsureCodes(trip, Random.Shared);

            foreach (var code in new[] { trip.SpellingBee.Game.GuestCode, trip.SpellingBee.Game.HostCode })
            {
                Assert.Equal(4, code.Length);
                Assert.DoesNotContain(code, c => "O0I1S5B8".Contains(c));
            }
        }
    }

    [Fact]
    public void Opening_the_wall_mints_codes_without_disturbing_the_game()
    {
        var trip = Started();
        Correct(trip);

        var before = trip.Scores.Count;
        Assert.False(SpellingBeeService.EnsureCodes(trip, Rng()));
        Assert.Equal(before, trip.Scores.Count);
        Assert.Equal(BeePhase.Spelling, trip.SpellingBee.Game.Phase);
    }

    // --------------------------------------------------------------- rotation

    [Fact]
    public void Turns_go_straight_down_the_row_and_wrap()
    {
        var trip = Started();
        var row = trip.SpellingBee.Game.Order;

        // Two full laps, so the wrap is exercised rather than assumed.
        Assert.Equal(row.Concat(row), Order(trip, row.Count * 2));
    }

    [Fact]
    public void The_row_never_reorders_itself()
    {
        var trip = Started();
        var row = trip.SpellingBee.Game.Order.ToList();

        Wrong(trip);
        Correct(trip);

        Assert.Equal(row, trip.SpellingBee.Game.Order);
    }

    [Fact]
    public void Somebody_out_is_stepped_over_rather_than_given_a_turn()
    {
        var trip = Started();
        var out1 = Speller(trip)!;

        Wrong(trip);

        Assert.Contains(out1, trip.SpellingBee.Game.Eliminated);
        Assert.DoesNotContain(out1, Order(trip, trip.SpellingBee.Game.Order.Count));
    }

    // ------------------------------------------------------------------ words

    [Fact]
    public void Words_are_never_reused()
    {
        var trip = Started();
        var seen = new List<string>();

        for (var i = 0; i < 12; i++)
        {
            seen.Add(SpellingBeeService.CurrentWord(trip)!.Word);
            Correct(trip);
        }

        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public void Skipping_burns_a_word_but_not_the_turn()
    {
        var trip = Started();
        var speller = Speller(trip);
        var word = SpellingBeeService.CurrentWord(trip)!.Word;

        SpellingBeeService.SkipWord(trip, Rng());

        Assert.Equal(speller, Speller(trip));
        Assert.NotEqual(word, SpellingBeeService.CurrentWord(trip)!.Word);
        Assert.Equal(BeePhase.Spelling, trip.SpellingBee.Game.Phase);
    }

    [Fact]
    public void A_skipped_word_never_comes_back()
    {
        var trip = Started();
        var skipped = new List<string>();

        // Skip a run of them, then play out a long stretch of the bee and check none reappear.
        for (var i = 0; i < 10; i++)
        {
            skipped.Add(SpellingBeeService.CurrentWord(trip)!.Word);
            SpellingBeeService.SkipWord(trip, Rng());
        }

        for (var i = 0; i < 40; i++) Correct(trip);

        var played = trip.SpellingBee.Words.Skip(skipped.Count).Select(w => w.Word);
        Assert.Empty(played.Intersect(skipped, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Words_come_out_of_the_tier_the_host_has_the_dial_on()
    {
        var trip = Trip();
        ReadyEveryone(trip);
        SpellingBeeService.SetStartingDifficulty(trip, "expert");
        SpellingBeeService.Start(trip, Rng());

        for (var i = 0; i < 15; i++) Correct(trip);

        Assert.All(trip.SpellingBee.Words, w => Assert.Equal("expert", w.TierKey));
    }

    [Fact]
    public void Starting_the_bee_opens_the_dial_where_the_wall_left_it()
    {
        var trip = Trip();
        ReadyEveryone(trip);
        SpellingBeeService.SetStartingDifficulty(trip, "easy");

        SpellingBeeService.Start(trip, Rng());

        Assert.Equal("easy", trip.SpellingBee.Game.DifficultyKey);
    }

    /// <summary>
    /// The dial moves the <em>next</em> word. Rewriting the one in play would change the word
    /// under a speller who has already been read it, which is the one thing a bee cannot do.
    /// </summary>
    [Fact]
    public void Shifting_the_difficulty_leaves_the_word_in_play_alone()
    {
        var trip = Started();
        var word = SpellingBeeService.CurrentWord(trip)!;

        SpellingBeeService.ShiftDifficulty(trip, 2);

        Assert.Same(word, SpellingBeeService.CurrentWord(trip));
        Assert.Equal("difficult", trip.SpellingBee.Game.DifficultyKey);

        Correct(trip);
        Assert.Equal("difficult", SpellingBeeService.CurrentWord(trip)!.TierKey);
    }

    [Fact]
    public void The_dial_stops_at_both_ends_rather_than_falling_off()
    {
        var trip = Started();

        for (var i = 0; i < 20; i++) SpellingBeeService.ShiftDifficulty(trip, -1);
        Assert.Equal(WordBank.Tiers[0].Key, trip.SpellingBee.Game.DifficultyKey);

        for (var i = 0; i < 20; i++) SpellingBeeService.ShiftDifficulty(trip, 1);
        Assert.Equal(WordBank.Tiers[^1].Key, trip.SpellingBee.Game.DifficultyKey);
    }

    /// <summary>
    /// Difficult holds under a hundred words, which a long bee left on that setting can genuinely
    /// empty. It has to keep dealing out of a neighbouring tier rather than handing the room a
    /// turn with no word in it.
    /// </summary>
    [Fact]
    public void A_tier_that_runs_dry_falls_out_to_its_neighbours()
    {
        var trip = Started();
        SpellingBeeService.SetStartingDifficulty(trip, "difficult");
        trip.SpellingBee.Game.DifficultyKey = "difficult";

        var draws = WordBank.Pool("difficult").Count + 20;
        for (var i = 0; i < draws; i++)
        {
            Assert.NotNull(SpellingBeeService.CurrentWord(trip));
            SpellingBeeService.SkipWord(trip, Rng());
        }

        var words = trip.SpellingBee.Words;
        Assert.Equal(words.Count, words.Select(w => w.Word.ToLowerInvariant()).Distinct().Count());
        Assert.Contains(words, w => w.TierKey != "difficult");
    }

    // ------------------------------------------------------------- elimination

    [Fact]
    public void Missing_a_word_puts_you_out_for_good()
    {
        var trip = Started();
        var gone = Speller(trip)!;

        SpellingBeeService.JudgeWrong(trip);

        Assert.False(trip.SpellingBee.Game.LastCorrect);
        Assert.Equal(gone, trip.SpellingBee.Game.JustEliminatedPersonId);
        Assert.Empty(trip.SpellingBee.Game.JustRevived);
        Assert.True(SpellingBeeService.IsOut(trip, gone));

        SpellingBeeService.Continue(trip, Rng());
        Assert.Null(trip.SpellingBee.Game.JustEliminatedPersonId);
    }

    // ---------------------------------------------------------------- revival

    /// <summary>
    /// The rule the whole endgame turns on: outlasting everybody is not winning. The last one
    /// standing has to put a word on the board, and until they do, moving on must not end the bee.
    /// </summary>
    [Fact]
    public void A_bee_is_never_won_by_outlasting()
    {
        var trip = Started(aCount: 1, bCount: 1);

        // One miss leaves one person standing, which under the old rule ended it on the spot.
        Wrong(trip);

        Assert.Single(SpellingBeeService.Survivors(trip));
        Assert.NotEqual(BeePhase.Finished, trip.SpellingBee.Game.Phase);
        Assert.Equal(BeePhase.Spelling, trip.SpellingBee.Game.Phase);
    }

    [Fact]
    public void The_last_one_standing_wins_by_spelling_their_word()
    {
        var trip = Started(aCount: 1, bCount: 1);
        Wrong(trip);

        var last = Speller(trip)!;
        Correct(trip);

        Assert.Equal(BeePhase.Finished, trip.SpellingBee.Game.Phase);
        Assert.Equal(last, SpellingBeeService.Winner(trip)?.Id);
    }

    [Fact]
    public void The_last_one_standing_missing_does_not_put_them_out()
    {
        var trip = Started(aCount: 1, bCount: 1);
        Wrong(trip);

        var last = Speller(trip)!;
        SpellingBeeService.JudgeWrong(trip);

        Assert.False(SpellingBeeService.IsOut(trip, last));
        Assert.Null(trip.SpellingBee.Game.JustEliminatedPersonId);
        Assert.NotEqual(BeePhase.Finished, trip.SpellingBee.Game.Phase);
    }

    /// <summary>
    /// Every team but the survivor's own gets its most recently eliminated member back. Their own
    /// team is deliberately left out — being the last of your own is meant to stay uncomfortable.
    /// </summary>
    [Fact]
    public void Every_other_team_gets_its_most_recent_loss_back()
    {
        var trip = Started();
        var row = trip.SpellingBee.Game.Order.ToList();

        // Everybody but the last in the row goes out, in row order.
        for (var i = 0; i < row.Count - 1; i++) Wrong(trip);

        var survivor = SpellingBeeService.Person(trip, Speller(trip))!;
        var lostBefore = trip.SpellingBee.Game.Eliminated.ToList();

        SpellingBeeService.JudgeWrong(trip);

        var revived = trip.SpellingBee.Game.JustRevived;
        Assert.NotEmpty(revived);

        // Nobody from the survivor's own team.
        Assert.All(revived, id =>
            Assert.NotEqual(survivor.TeamId, SpellingBeeService.Person(trip, id)!.TeamId));

        // Exactly one per other team, and each is that team's most recent loss.
        foreach (var team in trip.Teams.Where(t => t.Id != survivor.TeamId))
        {
            var lost = lostBefore.Where(id => SpellingBeeService.Person(trip, id)!.TeamId == team.Id).ToList();
            if (lost.Count == 0) continue;

            var backIn = Assert.Single(revived.Where(id =>
                SpellingBeeService.Person(trip, id)!.TeamId == team.Id));

            Assert.Equal(lost[^1], backIn);
            Assert.False(SpellingBeeService.IsOut(trip, backIn));
        }
    }

    [Fact]
    public void A_team_wiped_out_entirely_is_revived_too()
    {
        var trip = Started();
        var row = trip.SpellingBee.Game.Order.ToList();
        for (var i = 0; i < row.Count - 1; i++) Wrong(trip);

        var survivor = SpellingBeeService.Person(trip, Speller(trip))!;
        SpellingBeeService.JudgeWrong(trip);

        // Every other team had been emptied by that point, and every one of them has somebody now.
        foreach (var team in trip.Teams.Where(t => t.Id != survivor.TeamId))
        {
            Assert.Contains(SpellingBeeService.Survivors(trip), p => p.TeamId == team.Id);
        }
    }

    [Fact]
    public void Revived_players_keep_their_original_place_in_the_row()
    {
        var trip = Started();
        var row = trip.SpellingBee.Game.Order.ToList();
        for (var i = 0; i < row.Count - 1; i++) Wrong(trip);

        SpellingBeeService.JudgeWrong(trip);

        Assert.Equal(row, trip.SpellingBee.Game.Order);
    }

    [Fact]
    public void The_revival_card_clears_when_the_host_moves_on()
    {
        var trip = Started(aCount: 1, bCount: 1);
        Wrong(trip);
        SpellingBeeService.JudgeWrong(trip);

        Assert.NotEmpty(trip.SpellingBee.Game.JustRevived);

        SpellingBeeService.Continue(trip, Rng());

        Assert.Empty(trip.SpellingBee.Game.JustRevived);
        Assert.Equal(BeePhase.Spelling, trip.SpellingBee.Game.Phase);
    }

    /// <summary>
    /// With everybody still involved on one team there is no other team to draw from, so the
    /// refill comes up empty. Ending is the only honest answer: leaving it would hand the last
    /// speller a turn they cannot lose and a bee that never finishes.
    /// </summary>
    [Fact]
    public void With_nobody_left_to_bring_back_the_bee_ends()
    {
        var trip = Started(aCount: 2, bCount: 0);

        Wrong(trip);
        var last = Speller(trip)!;
        Wrong(trip);

        Assert.Equal(BeePhase.Finished, trip.SpellingBee.Game.Phase);
        Assert.Equal(last, SpellingBeeService.Winner(trip)?.Id);
        Assert.Empty(trip.SpellingBee.Game.JustRevived);
    }

    // ------------------------------------------------------------ reinstating

    [Fact]
    public void Somebody_put_back_in_keeps_their_place_in_the_row()
    {
        var trip = Started();
        var row = trip.SpellingBee.Game.Order.ToList();
        var gone = Speller(trip)!;

        Wrong(trip);
        SpellingBeeService.Reinstate(trip, gone);

        Assert.False(SpellingBeeService.IsOut(trip, gone));
        Assert.Equal(row, trip.SpellingBee.Game.Order);
        Assert.Contains(gone, Order(trip, row.Count));
    }

    [Fact]
    public void Reinstating_somebody_who_is_already_in_changes_nothing()
    {
        var trip = Started();
        var before = trip.SpellingBee.Game.Eliminated.ToList();

        SpellingBeeService.Reinstate(trip, "ann");

        Assert.Equal(before, trip.SpellingBee.Game.Eliminated);
    }

    [Fact]
    public void A_finished_bee_cannot_be_reopened_by_putting_somebody_back()
    {
        var trip = Started(aCount: 1, bCount: 1);
        var gone = Speller(trip)!;

        Wrong(trip);
        Correct(trip);   // the last one standing earns it, which is the only way to finish

        SpellingBeeService.Reinstate(trip, gone);

        Assert.Equal(BeePhase.Finished, trip.SpellingBee.Game.Phase);
        Assert.True(SpellingBeeService.IsOut(trip, gone));
    }

    // ---------------------------------------------------------------- scoring

    [Fact]
    public void Every_correct_word_pays_the_spellers_team_as_it_happens()
    {
        var trip = Started();
        var first = SpellingBeeService.Person(trip, Speller(trip))!;

        SpellingBeeService.JudgeCorrect(trip, Now);

        var entry = Assert.Single(trip.Scores);
        Assert.Equal(SpellingBeeService.GameId, entry.GameId);
        Assert.Equal(first.TeamId, entry.TeamId);
        Assert.Equal(trip.SpellingBee.PointsPerWord, entry.Points);
        Assert.Contains(first.Name, entry.Note);
    }

    [Fact]
    public void Missing_a_word_pays_nothing()
    {
        var trip = Started();

        SpellingBeeService.JudgeWrong(trip);

        Assert.Empty(trip.Scores);
    }

    [Fact]
    public void Points_banked_before_being_knocked_out_still_count()
    {
        var trip = Started();
        var speller = SpellingBeeService.Person(trip, Speller(trip))!;

        Correct(trip);
        while (Speller(trip) != speller.Id) Correct(trip);
        Wrong(trip);

        Assert.True(SpellingBeeService.IsOut(trip, speller.Id));
        Assert.Equal(trip.SpellingBee.PointsPerWord, SpellingBeeService.ScoreFor(trip, speller.TeamId)
            - trip.Scores.Where(s => s.TeamId == speller.TeamId).Skip(1).Sum(s => s.Points));
        Assert.True(SpellingBeeService.ScoreFor(trip, speller.TeamId) >= trip.SpellingBee.PointsPerWord);
    }

    /// <summary>
    /// Winning is worth nothing on its own. The only points in this bee are the five a word pays,
    /// so a winner who missed their way to the end and spelled exactly one word takes exactly five.
    /// </summary>
    [Fact]
    public void Winning_pays_no_bonus_beyond_the_words()
    {
        var trip = Started(aCount: 1, bCount: 1);

        Wrong(trip);
        var winner = SpellingBeeService.Person(trip, Speller(trip))!;
        Correct(trip);

        Assert.Equal(BeePhase.Finished, trip.SpellingBee.Game.Phase);

        var entry = Assert.Single(trip.Scores);
        Assert.Equal(winner.TeamId, entry.TeamId);
        Assert.Equal(trip.SpellingBee.PointsPerWord, entry.Points);
    }

    [Fact]
    public void Reset_clears_the_bee_and_leaves_other_games_alone()
    {
        var trip = Started();
        Correct(trip);

        trip.Scores.Add(new ScoreEntry { Id = "keep", GameId = "jeopardy", TeamId = "a", Points = 15 });

        SpellingBeeService.Reset(trip, Rng());

        Assert.Equal(BeePhase.NotStarted, trip.SpellingBee.Game.Phase);
        Assert.Empty(trip.SpellingBee.Game.Order);
        Assert.Empty(trip.SpellingBee.Words);
        Assert.Equal("keep", Assert.Single(trip.Scores).Id);
        Assert.NotEmpty(trip.SpellingBee.Game.GuestCode);
    }
}
