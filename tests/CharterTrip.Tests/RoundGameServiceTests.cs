using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class RoundGameServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 14, 0, 0, TimeSpan.Zero);

    private static TripData Jake() => new()
    {
        Teams =
        [
            new Team { Id = "jou",  Name = "Team Jou" },
            new Team { Id = "ali",  Name = "Team Ali" },
            new Team { Id = "kyle", Name = "Team Kyle" },
            new Team { Id = "em",   Name = "Team Em" }
        ]
    };

    private static RoundGame Sketch() => new()
    {
        PointValue = 20,
        RoundCount = 3,
        Characters =
        [
            new SketchCharacter { Name = "Shrek" },
            new SketchCharacter { Name = "Snoopy" },
            new SketchCharacter { Name = "Pikachu" }
        ]
    };

    private static RoundGame Cups() => new() { PointValue = 10, RoundCount = 2 };

    /// <summary>Score a round of cups: who knocked off how many, then on to the next one.</summary>
    private static void Cup(TripData trip, RoundGame game, params (string Team, int Count)[] counts) =>
        RoundGameService.AwardRoundCounts(
            trip, game, RoundGameService.NoodleCupId,
            counts.ToDictionary(c => c.Team, c => c.Count), "cups", T0);

    [Fact]
    public void Beginning_a_game_opens_round_one()
    {
        var game = Sketch();
        game.UsedCharacters.Add("Shrek");

        RoundGameService.Begin(game);

        Assert.Equal(PartyGamePhase.Playing, game.Phase);
        Assert.Equal(1, game.Round);
        Assert.Empty(game.UsedCharacters);
        Assert.Null(game.CurrentCharacter);
    }

    [Fact]
    public void A_picked_character_is_spent_and_stops_coming_up()
    {
        var game = Sketch();
        RoundGameService.Begin(game);

        RoundGameService.PickCharacter(game, "Snoopy");

        Assert.Equal("Snoopy", game.CurrentCharacter);
        Assert.Equal(["Shrek", "Pikachu"], RoundGameService.RemainingCharacters(game).Select(c => c.Name));
    }

    [Fact]
    public void The_round_winner_takes_the_point_value_and_the_game_moves_on()
    {
        var trip = Jake();
        var game = Sketch();
        RoundGameService.Begin(game);
        RoundGameService.PickCharacter(game, "Shrek");

        RoundGameService.AwardRoundWinner(trip, game, RoundGameService.SketchId, "kyle", T0);

        var entry = Assert.Single(trip.Scores);
        Assert.Equal(20, entry.Points);
        Assert.Equal("kyle", entry.TeamId);
        Assert.Equal("Round 1 · Shrek", entry.Note);
        Assert.Equal(2, game.Round);
        Assert.Null(game.CurrentCharacter);
    }

    [Fact]
    public void Counts_are_multiplied_by_the_point_value_per_team()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        RoundGameService.AwardRoundCounts(
            trip, game, RoundGameService.NoodleCupId,
            new Dictionary<string, int> { ["kyle"] = 3, ["em"] = 1 }, "cups", T0);

        Assert.Equal(30, ScoreService.ScoreFor(trip, RoundGameService.NoodleCupId, "kyle"));
        Assert.Equal(10, ScoreService.ScoreFor(trip, RoundGameService.NoodleCupId, "em"));
        Assert.Equal(2, game.Round);
    }

    [Fact]
    public void A_team_that_scored_nothing_is_not_written_down()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        RoundGameService.AwardRoundCounts(
            trip, game, RoundGameService.NoodleCupId,
            new Dictionary<string, int> { ["kyle"] = 2, ["em"] = 0, ["jou"] = 0 }, "cups", T0);

        Assert.Single(trip.Scores);
        Assert.Equal("kyle", trip.Scores[0].TeamId);
    }

    [Fact]
    public void Past_the_last_round_the_game_is_over_rather_than_on_round_three()
    {
        var trip = Jake();
        var game = Cups();          // two rounds
        RoundGameService.Begin(game);

        Cup(trip, game, ("kyle", 2));
        Assert.Equal(PartyGamePhase.Playing, game.Phase);

        Cup(trip, game, ("kyle", 1));

        Assert.Equal(PartyGamePhase.Finished, game.Phase);
    }

    // ------------------------------------------------------------------ what is in a round

    private static RoundGame Beers() => new() { PointValue = 10, RoundCount = 4, TakeToWin = 4 };

    /// <summary>Four for the winner and three each for the other three corners.</summary>
    [Fact]
    public void The_stack_is_thirteen_for_four_beers_across_four_corners()
    {
        var trip = Jake();          // four teams

        Assert.Equal(13, RoundGameService.RoundPool(trip, Beers()));
    }

    /// <summary>
    /// The stack is the one size that works: big enough that somebody must reach four, small
    /// enough that only one of them can. Both bounds land on the same number, so there is no
    /// range to choose from — which is why it is worked out rather than typed in.
    /// </summary>
    [Theory]
    [InlineData(4, 4, 13)]
    [InlineData(4, 3, 10)]
    [InlineData(4, 2, 7)]
    [InlineData(3, 4, 9)]
    [InlineData(5, 4, 17)]
    public void The_stack_guarantees_one_winner_and_only_one(int win, int teams, int stack)
    {
        var trip = Jake();
        trip.Teams.RemoveRange(teams, trip.Teams.Count - teams);
        var game = new RoundGame { PointValue = 10, RoundCount = 4, TakeToWin = win };

        Assert.Equal(stack, RoundGameService.RoundPool(trip, game));

        // Every corner one short of winning cannot account for the whole stack, so somebody
        // has to get there...
        Assert.True((win - 1) * teams < stack);

        // ...and one winner plus everybody else stopping short accounts for it exactly, so a
        // second corner never has to.
        Assert.Equal(stack, win + (win - 1) * (teams - 1));
    }

    /// <summary>A game with nothing to win at is one each, which is where the cups started.</summary>
    [Fact]
    public void A_game_with_nothing_to_win_at_is_one_each()
    {
        var trip = Jake();

        Assert.Equal(trip.Teams.Count, RoundGameService.RoundPool(trip, Cups()));
    }

    [Fact]
    public void The_stack_follows_the_teams_when_one_is_dropped()
    {
        var trip = Jake();
        trip.Teams.RemoveAt(0);

        Assert.Equal(3, RoundGameService.RoundPool(trip, Cups()));
        Assert.Equal(10, RoundGameService.RoundPool(trip, Beers()));
    }

    [Fact]
    public void No_team_can_be_credited_with_more_than_the_number_that_wins_it()
    {
        var trip = Jake();

        Assert.Equal(4, RoundGameService.MostOneTeamCanTake(trip, Beers()));
    }

    /// <summary>Nothing stops one team taking every cup, so the cap is the whole stack.</summary>
    [Fact]
    public void A_game_with_nothing_to_win_at_lets_one_team_take_the_lot()
    {
        var trip = Jake();

        Assert.Equal(trip.Teams.Count, RoundGameService.MostOneTeamCanTake(trip, Cups()));
    }

    // ------------------------------------------------------------------ finishing level

    /// <summary>The rounds it was set up with and nothing more — a shared top is the result.</summary>
    [Fact]
    public void Ending_level_at_the_top_finishes_the_game_anyway()
    {
        var trip = Jake();
        var game = Cups();          // two rounds
        RoundGameService.Begin(game);

        Cup(trip, game, ("kyle", 2));
        Cup(trip, game, ("em", 2));          // both on 20 after the last scheduled round

        Assert.Equal(PartyGamePhase.Finished, game.Phase);
        Assert.Equal(2, game.Round);         // not a third
    }

    [Fact]
    public void A_game_that_ends_level_has_two_leaders_and_no_leader()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        Cup(trip, game, ("kyle", 2), ("jou", 1));
        Cup(trip, game, ("em", 2));

        Assert.Equal(
            ["em", "kyle"],
            ScoreService.Leaders(trip, RoundGameService.NoodleCupId).Select(t => t.Id).Order());
        Assert.Null(ScoreService.Leader(trip, RoundGameService.NoodleCupId));
    }

    [Fact]
    public void Somebody_ahead_at_the_end_wins_it_outright()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        Cup(trip, game, ("kyle", 2));
        Cup(trip, game, ("em", 1));

        Assert.Equal(PartyGamePhase.Finished, game.Phase);
        Assert.Equal("kyle", ScoreService.Leader(trip, RoundGameService.NoodleCupId)?.Id);
    }

    /// <summary>Every round is scored to everybody: nothing ever narrows the field.</summary>
    [Fact]
    public void A_late_round_still_pays_a_team_that_was_behind()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        Cup(trip, game, ("kyle", 2));
        Cup(trip, game, ("jou", 2));

        Assert.Equal(20, ScoreService.ScoreFor(trip, RoundGameService.NoodleCupId, "jou"));
    }

    [Fact]
    public void A_skipped_round_moves_on_without_paying_anybody()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        RoundGameService.NextRound(game);

        Assert.Equal(2, game.Round);
        Assert.Empty(trip.Scores);
    }

    [Fact]
    public void Nothing_is_scored_before_the_game_starts_or_after_it_ends()
    {
        var trip = Jake();
        var game = Sketch();        // still NotStarted

        RoundGameService.AwardRoundWinner(trip, game, RoundGameService.SketchId, "kyle", T0);
        Assert.Empty(trip.Scores);

        game.Phase = PartyGamePhase.Finished;
        RoundGameService.AwardRoundWinner(trip, game, RoundGameService.SketchId, "kyle", T0);
        Assert.Empty(trip.Scores);
    }

    [Fact]
    public void Reset_gives_the_points_back_but_keeps_the_setup()
    {
        var trip = Jake();
        var game = Sketch();
        RoundGameService.Begin(game);
        RoundGameService.PickCharacter(game, "Shrek");
        RoundGameService.AwardRoundWinner(trip, game, RoundGameService.SketchId, "kyle", T0);
        ScoreService.Award(trip, RoundGameService.NoodleCupId, "kyle", 30, "", T0);

        RoundGameService.Reset(trip, game, RoundGameService.SketchId);

        Assert.Equal(PartyGamePhase.NotStarted, game.Phase);
        Assert.Equal(1, game.Round);
        Assert.Empty(game.UsedCharacters);
        Assert.Equal(0, ScoreService.ScoreFor(trip, RoundGameService.SketchId, "kyle"));

        // The settings are how the game is set up, not how it went.
        Assert.Equal(20, game.PointValue);
        Assert.Equal(3, game.Characters.Count);

        // And another game's points are none of its business.
        Assert.Equal(30, ScoreService.ScoreFor(trip, RoundGameService.NoodleCupId, "kyle"));
    }
}
