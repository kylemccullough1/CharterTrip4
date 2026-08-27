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
        Prompts = ["Shrek", "Snoopy", "Pikachu"]
    };

    private static RoundGame Cups() => new() { PointValue = 10, RoundCount = 2 };

    [Fact]
    public void Beginning_a_game_opens_round_one()
    {
        var game = Sketch();
        game.UsedPrompts.Add("Shrek");

        RoundGameService.Begin(game);

        Assert.Equal(PartyGamePhase.Playing, game.Phase);
        Assert.Equal(1, game.Round);
        Assert.Empty(game.UsedPrompts);
        Assert.Null(game.CurrentPrompt);
    }

    [Fact]
    public void A_picked_character_is_spent_and_stops_coming_up()
    {
        var game = Sketch();
        RoundGameService.Begin(game);

        RoundGameService.PickPrompt(game, "Snoopy");

        Assert.Equal("Snoopy", game.CurrentPrompt);
        Assert.Equal(["Shrek", "Pikachu"], RoundGameService.RemainingPrompts(game));
    }

    [Fact]
    public void The_round_winner_takes_the_point_value_and_the_game_moves_on()
    {
        var trip = Jake();
        var game = Sketch();
        RoundGameService.Begin(game);
        RoundGameService.PickPrompt(game, "Shrek");

        RoundGameService.AwardRoundWinner(trip, game, RoundGameService.SketchId, "kyle", T0);

        var entry = Assert.Single(trip.Scores);
        Assert.Equal(20, entry.Points);
        Assert.Equal("kyle", entry.TeamId);
        Assert.Equal("Round 1 · Shrek", entry.Note);
        Assert.Equal(2, game.Round);
        Assert.Null(game.CurrentPrompt);
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
    public void Past_the_last_round_the_game_is_over_rather_than_on_round_four()
    {
        var trip = Jake();
        var game = Cups();          // two rounds
        RoundGameService.Begin(game);

        RoundGameService.AwardRoundCounts(
            trip, game, RoundGameService.NoodleCupId, new Dictionary<string, int> { ["kyle"] = 1 }, "cups", T0);
        Assert.Equal(PartyGamePhase.Playing, game.Phase);

        RoundGameService.AwardRoundCounts(
            trip, game, RoundGameService.NoodleCupId, new Dictionary<string, int> { ["em"] = 1 }, "cups", T0);

        Assert.Equal(PartyGamePhase.Finished, game.Phase);
        Assert.Equal(2, game.Round);
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
        RoundGameService.PickPrompt(game, "Shrek");
        RoundGameService.AwardRoundWinner(trip, game, RoundGameService.SketchId, "kyle", T0);
        ScoreService.Award(trip, RoundGameService.NoodleCupId, "kyle", 30, "", T0);

        RoundGameService.Reset(trip, game, RoundGameService.SketchId);

        Assert.Equal(PartyGamePhase.NotStarted, game.Phase);
        Assert.Equal(1, game.Round);
        Assert.Empty(game.UsedPrompts);
        Assert.Equal(0, ScoreService.ScoreFor(trip, RoundGameService.SketchId, "kyle"));

        // The settings are how the game is set up, not how it went.
        Assert.Equal(20, game.PointValue);
        Assert.Equal(3, game.Prompts.Count);

        // And another game's points are none of its business.
        Assert.Equal(30, ScoreService.ScoreFor(trip, RoundGameService.NoodleCupId, "kyle"));
    }
}
