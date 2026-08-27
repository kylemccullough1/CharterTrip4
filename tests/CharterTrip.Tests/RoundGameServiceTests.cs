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
        Assert.False(game.IsSuddenDeath);
    }

    // --------------------------------------------------------------- sudden death

    [Fact]
    public void Ending_level_at_the_top_sends_the_tied_teams_to_sudden_death()
    {
        var trip = Jake();
        var game = Cups();          // two rounds
        RoundGameService.Begin(game);

        Cup(trip, game, ("kyle", 2));
        Cup(trip, game, ("em", 2));          // both on 20 after the last scheduled round

        Assert.Equal(PartyGamePhase.Playing, game.Phase);
        Assert.True(game.IsSuddenDeath);
        Assert.Equal(["em", "kyle"], game.TieBreakTeamIds.Order());
    }

    [Fact]
    public void Only_the_tied_teams_are_in_play_once_it_goes_to_sudden_death()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        Assert.Equal(4, RoundGameService.ActiveTeams(trip, game).Count);

        Cup(trip, game, ("kyle", 2));
        Cup(trip, game, ("em", 2));

        Assert.Equal(["em", "kyle"], RoundGameService.ActiveTeams(trip, game).Select(t => t.Id).Order());
    }

    /// <summary>A team that was behind when the whistle went does not get a second chance.</summary>
    [Fact]
    public void Only_the_teams_actually_level_go_through()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        Cup(trip, game, ("kyle", 2), ("em", 2), ("jou", 1));
        Cup(trip, game);        // nobody scored in the second round

        Assert.True(game.IsSuddenDeath);
        Assert.Equal(["em", "kyle"], game.TieBreakTeamIds.Order());
        Assert.DoesNotContain("jou", game.TieBreakTeamIds);
    }

    [Fact]
    public void Sudden_death_ends_the_game_the_moment_somebody_is_ahead()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        Cup(trip, game, ("kyle", 2));
        Cup(trip, game, ("em", 2));
        Cup(trip, game, ("em", 1));          // em breaks it

        Assert.Equal(PartyGamePhase.Finished, game.Phase);
        Assert.False(game.IsSuddenDeath);
        Assert.Equal("em", ScoreService.Leader(trip, RoundGameService.NoodleCupId)?.Id);
    }

    [Fact]
    public void Sudden_death_that_ends_level_goes_round_again()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        Cup(trip, game, ("kyle", 2));
        Cup(trip, game, ("em", 2));
        Cup(trip, game, ("kyle", 1), ("em", 1));      // still level

        Assert.Equal(PartyGamePhase.Playing, game.Phase);
        Assert.True(game.IsSuddenDeath);
        Assert.Equal(["em", "kyle"], game.TieBreakTeamIds.Order());
    }

    /// <summary>However long it takes — the scoreboard has no room for a joint first.</summary>
    [Fact]
    public void A_finished_round_game_always_has_exactly_one_leader()
    {
        var trip = Jake();
        var game = Cups();
        RoundGameService.Begin(game);

        Cup(trip, game, ("kyle", 2));
        Cup(trip, game, ("em", 2));
        Cup(trip, game, ("kyle", 1), ("em", 1));
        Cup(trip, game, ("kyle", 1), ("em", 1));
        Cup(trip, game, ("kyle", 3));

        Assert.Equal(PartyGamePhase.Finished, game.Phase);
        Assert.Equal("kyle", ScoreService.Leader(trip, RoundGameService.NoodleCupId)?.Id);
    }

    [Fact]
    public void A_sketch_award_in_sudden_death_says_so()
    {
        var trip = Jake();
        var game = new RoundGame { PointValue = 20, RoundCount = 1 };
        RoundGameService.Begin(game);

        // One round, two teams level on nothing, so it goes straight to sudden death.
        RoundGameService.NextRound(trip, game, RoundGameService.SketchId);
        Assert.True(game.IsSuddenDeath);

        RoundGameService.AwardRoundWinner(trip, game, RoundGameService.SketchId, "kyle", T0);

        Assert.Equal("Sudden death", trip.Scores[^1].Note);
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
        Assert.Empty(game.TieBreakTeamIds);
        Assert.Equal(0, ScoreService.ScoreFor(trip, RoundGameService.SketchId, "kyle"));

        // The settings are how the game is set up, not how it went.
        Assert.Equal(20, game.PointValue);
        Assert.Equal(3, game.Characters.Count);

        // And another game's points are none of its business.
        Assert.Equal(30, ScoreService.ScoreFor(trip, RoundGameService.NoodleCupId, "kyle"));
    }
}
