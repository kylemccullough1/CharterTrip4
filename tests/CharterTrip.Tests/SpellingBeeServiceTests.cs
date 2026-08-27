using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class SpellingBeeServiceTests
{
    /// <summary>
    /// Two teams of deliberately different size, because every interesting rule in the bee is
    /// about what happens when they are uneven. A has three, B has one.
    /// </summary>
    private static TripData Trip(int aCount = 3, int bCount = 1, int words = 40)
    {
        var trip = new TripData
        {
            Teams =
            [
                new Team { Id = "a", Name = "Team A" },
                new Team { Id = "b", Name = "Team B" }
            ]
        };

        // Ann, Ben, Cal … then Dee, Eve, Fay …
        var names = new[] { "Ann", "Ben", "Cal", "Gus", "Hal" };
        var bNames = new[] { "Dee", "Eve", "Fay", "Ivy", "Joy" };

        for (var i = 0; i < aCount; i++)
            trip.Roster.Add(new RosterPerson { Id = names[i].ToLowerInvariant(), Name = names[i], TeamId = "a" });
        for (var i = 0; i < bCount; i++)
            trip.Roster.Add(new RosterPerson { Id = bNames[i].ToLowerInvariant(), Name = bNames[i], TeamId = "b" });

        for (var i = 1; i <= words; i++)
            trip.SpellingBee.Words.Add(new BeeWord { Id = $"w{i}", Word = $"word{i}" });

        return trip;
    }

    private static string? Speller(TripData trip) => trip.SpellingBee.Game.CurrentPersonId;

    /// <summary>Spell the current word right and move past the reveal.</summary>
    private static void Correct(TripData trip)
    {
        SpellingBeeService.JudgeCorrect(trip);
        SpellingBeeService.Continue(trip);
    }

    /// <summary>Miss the current word and move past the reveal.</summary>
    private static void Wrong(TripData trip)
    {
        SpellingBeeService.JudgeWrong(trip);
        SpellingBeeService.Continue(trip);
    }

    /// <summary>The next <paramref name="turns"/> spellers, everyone getting their word right.</summary>
    private static List<string> Order(TripData trip, int turns)
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
    public void Start_puts_everyone_on_a_team_in()
    {
        var trip = Trip();
        SpellingBeeService.Start(trip);

        Assert.Equal(4, trip.SpellingBee.Game.Survivors.Count);
        Assert.Equal(BeePhase.Spelling, trip.SpellingBee.Game.Phase);
        Assert.NotNull(Speller(trip));
    }

    [Fact]
    public void Start_leaves_out_anyone_without_a_team()
    {
        var trip = Trip();
        trip.Roster.Add(new RosterPerson { Id = "zed", Name = "Zed", TeamId = "" });

        SpellingBeeService.Start(trip);

        // A speller with nowhere to send the points has no place in the rotation.
        Assert.DoesNotContain("zed", trip.SpellingBee.Game.Survivors);
    }

    // --------------------------------------------------------------- rotation

    /// <summary>
    /// The rule the whole design hangs off. Rotating a plain queue would give
    /// Ann, Dee, Ben, Cal — team B's lone member must get every other turn instead.
    /// </summary>
    [Fact]
    public void Turns_rotate_by_team_not_by_person()
    {
        var trip = Trip(aCount: 3, bCount: 1);
        SpellingBeeService.Start(trip);

        Assert.Equal(["ann", "dee", "ben", "dee", "cal", "dee"], Order(trip, 6));
    }

    [Fact]
    public void A_team_passes_its_turn_down_its_own_members_and_wraps()
    {
        var trip = Trip(aCount: 3, bCount: 1);
        SpellingBeeService.Start(trip);

        // Team A's turns, taken every other go, cycle Ann → Ben → Cal → Ann.
        var teamATurns = Order(trip, 8).Where((_, i) => i % 2 == 0).ToList();
        Assert.Equal(["ann", "ben", "cal", "ann"], teamATurns);
    }

    [Fact]
    public void A_team_with_nobody_left_is_skipped()
    {
        var trip = Trip(aCount: 3, bCount: 1);
        SpellingBeeService.Start(trip);

        // Ann spells, then Dee — team B's only member — misses and is out.
        Correct(trip);
        Assert.Equal("dee", Speller(trip));
        Wrong(trip);

        // With B empty the rotation is team A's alone, and it does not stall on the dead team.
        Assert.Equal(["ben", "cal", "ann"], Order(trip, 3));
    }

    // ------------------------------------------------------------------ words

    [Fact]
    public void Words_are_never_reused()
    {
        var trip = Trip();
        SpellingBeeService.Start(trip);

        var seen = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            seen.Add(SpellingBeeService.CurrentWord(trip)!.Id);
            Correct(trip);
        }

        Assert.Equal(seen.Distinct().Count(), seen.Count);
    }

    [Fact]
    public void Skipping_burns_a_word_but_not_the_turn()
    {
        var trip = Trip();
        SpellingBeeService.Start(trip);

        var speller = Speller(trip);
        var first = SpellingBeeService.CurrentWord(trip)!.Id;

        SpellingBeeService.SkipWord(trip);

        Assert.NotEqual(first, SpellingBeeService.CurrentWord(trip)!.Id);
        Assert.Equal(speller, Speller(trip));
        Assert.Equal(BeePhase.Spelling, trip.SpellingBee.Game.Phase);
    }

    [Fact]
    public void Words_remaining_counts_down()
    {
        var trip = Trip(words: 10);
        SpellingBeeService.Start(trip);

        Assert.Equal(10, SpellingBeeService.WordsRemaining(trip));
        Correct(trip);
        Assert.Equal(9, SpellingBeeService.WordsRemaining(trip));
    }

    // --------------------------------------------------------------- revival

    /// <summary>
    /// Walk the bee down to one survivor. Team B's Dee goes first, then A's members in turn,
    /// leaving whoever is named last standing alone.
    /// </summary>
    private static TripData DownToOne()
    {
        var trip = Trip(aCount: 3, bCount: 1);
        SpellingBeeService.Start(trip);

        Wrong(trip);   // ann out
        Wrong(trip);   // dee out — team B is empty
        Wrong(trip);   // ben out — only cal is left

        Assert.Equal(["cal"], trip.SpellingBee.Game.Survivors);
        return trip;
    }

    [Fact]
    public void The_last_one_standing_wins_only_by_spelling()
    {
        var trip = DownToOne();

        Correct(trip);

        Assert.Equal(BeePhase.Finished, trip.SpellingBee.Game.Phase);
        Assert.Equal("Cal", SpellingBeeService.Winner(trip)?.Name);
    }

    [Fact]
    public void The_last_one_standing_missing_brings_a_member_of_each_team_back()
    {
        var trip = DownToOne();

        SpellingBeeService.JudgeWrong(trip);

        // Ben was A's most recent loss, Dee was B's only one. Ann, out first, stays out.
        Assert.Equal(["ben", "dee"], trip.SpellingBee.Game.JustRevived.Order());
        Assert.Contains("ben", trip.SpellingBee.Game.Survivors);
        Assert.Contains("dee", trip.SpellingBee.Game.Survivors);
        Assert.DoesNotContain("ann", trip.SpellingBee.Game.Survivors);
    }

    [Fact]
    public void The_speller_who_triggered_the_revival_stays_in()
    {
        var trip = DownToOne();

        SpellingBeeService.JudgeWrong(trip);

        // Eliminating them would empty the field, which is the very thing the rule prevents.
        Assert.Contains("cal", trip.SpellingBee.Game.Survivors);
        Assert.DoesNotContain("cal", trip.SpellingBee.Game.Eliminated);
        Assert.NotEqual(BeePhase.Finished, trip.SpellingBee.Game.Phase);
    }

    [Fact]
    public void A_wiped_out_team_is_revived_too()
    {
        var trip = DownToOne();

        // Team B had nobody left at all before this.
        Assert.Empty(SpellingBeeService.SurvivorsOn(trip, "b"));

        SpellingBeeService.JudgeWrong(trip);

        Assert.Single(SpellingBeeService.SurvivorsOn(trip, "b"));
    }

    [Fact]
    public void The_bee_resumes_normally_after_a_revival()
    {
        var trip = DownToOne();
        Wrong(trip);

        // Three back in play across two teams, so the team rotation picks up where it left off.
        Assert.Equal(3, trip.SpellingBee.Game.Survivors.Count);
        Assert.Equal(BeePhase.Spelling, trip.SpellingBee.Game.Phase);
        Assert.NotNull(Speller(trip));
    }

    [Fact]
    public void The_revival_clears_once_the_host_moves_on()
    {
        var trip = DownToOne();

        SpellingBeeService.JudgeWrong(trip);
        Assert.NotEmpty(trip.SpellingBee.Game.JustRevived);

        SpellingBeeService.Continue(trip);
        Assert.Empty(trip.SpellingBee.Game.JustRevived);
    }

    [Fact]
    public void A_solo_field_with_nobody_to_revive_ends_rather_than_hanging()
    {
        var trip = Trip(aCount: 1, bCount: 0);
        SpellingBeeService.Start(trip);

        SpellingBeeService.JudgeWrong(trip);

        // There is no one to bring back, so an unloseable turn would repeat forever.
        Assert.Equal(BeePhase.Finished, trip.SpellingBee.Game.Phase);
    }

    // --------------------------------------------------------------- scoring

    [Fact]
    public void The_winners_team_takes_the_points_once()
    {
        var trip = DownToOne();
        trip.SpellingBee.WinnerPoints = 10;

        Correct(trip);

        var entry = Assert.Single(trip.Scores);
        Assert.Equal("a", entry.TeamId);
        Assert.Equal("spelling", entry.GameId);
        Assert.Equal(10, entry.Points);
        Assert.Equal(10, SpellingBeeService.ScoreFor(trip, "a"));
        Assert.Equal(0, SpellingBeeService.ScoreFor(trip, "b"));
    }

    [Fact]
    public void Nothing_is_scored_before_the_bee_is_over()
    {
        var trip = Trip();
        SpellingBeeService.Start(trip);

        Order(trip, 6);

        Assert.Empty(trip.Scores);
    }

    [Fact]
    public void Reset_clears_the_bee_and_leaves_other_games_alone()
    {
        var trip = DownToOne();
        Correct(trip);

        trip.Scores.Add(new ScoreEntry { Id = "j", TeamId = "a", GameId = "jeopardy", Points = 25 });

        SpellingBeeService.Reset(trip);

        Assert.Equal(BeePhase.NotStarted, trip.SpellingBee.Game.Phase);
        Assert.Empty(trip.SpellingBee.Game.Survivors);
        Assert.Empty(trip.SpellingBee.Game.Eliminated);
        Assert.Equal(0, trip.SpellingBee.Game.WordCursor);

        var entry = Assert.Single(trip.Scores);   // jeopardy is untouched
        Assert.Equal("jeopardy", entry.GameId);
    }
}
