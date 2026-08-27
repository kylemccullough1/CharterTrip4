using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class ScoreServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 14, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int seconds) => T0.AddSeconds(seconds);

    /// <summary>Teams in the order they are stored: JAKE — Jou, Ali, Kyle, Em.</summary>
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

    [Fact]
    public void Award_logs_the_entry_and_hands_it_back()
    {
        var trip = Jake();

        var entry = ScoreService.Award(trip, "sketch", "kyle", 20, "Round 1", T0);

        Assert.Single(trip.Scores);
        Assert.Equal("sketch", entry.GameId);
        Assert.Equal("kyle", entry.TeamId);
        Assert.Equal(20, entry.Points);
        Assert.Equal("Round 1", entry.Note);
        Assert.NotEmpty(entry.Id);
        Assert.Same(trip.Scores[0], entry);
    }

    [Fact]
    public void Points_can_be_negative_so_an_over_award_can_be_taken_back()
    {
        var trip = Jake();
        ScoreService.Award(trip, "sketch", "kyle", 20, "Round 1", T0);
        ScoreService.Award(trip, "sketch", "kyle", -20, "Miscounted", At(5));

        Assert.Equal(0, ScoreService.ScoreFor(trip, "sketch", "kyle"));
    }

    [Fact]
    public void A_score_counts_only_this_game_and_this_team()
    {
        var trip = Jake();
        ScoreService.Award(trip, "sketch", "kyle", 20, "", T0);
        ScoreService.Award(trip, "noodlecup", "kyle", 50, "", T0);
        ScoreService.Award(trip, "sketch", "em", 20, "", T0);

        Assert.Equal(20, ScoreService.ScoreFor(trip, "sketch", "kyle"));
        Assert.Equal(50, ScoreService.ScoreFor(trip, "noodlecup", "kyle"));
        Assert.Equal(0, ScoreService.ScoreFor(trip, "beerrun", "kyle"));
    }

    [Fact]
    public void The_scoreboard_keeps_every_team_including_the_ones_on_nothing()
    {
        var trip = Jake();
        ScoreService.Award(trip, "sketch", "em", 20, "", T0);

        var board = ScoreService.Scoreboard(trip, "sketch");

        Assert.Equal(["jou", "ali", "kyle", "em"], board.Select(r => r.Team.Id));
        Assert.Equal([0, 0, 0, 20], board.Select(r => r.Score));
    }

    [Fact]
    public void Undo_removes_that_one_award_and_nothing_else()
    {
        var trip = Jake();
        var first = ScoreService.Award(trip, "sketch", "kyle", 20, "", T0);
        ScoreService.Award(trip, "sketch", "kyle", 20, "", At(5));
        ScoreService.Award(trip, "noodlecup", "kyle", 30, "", At(10));

        ScoreService.Undo(trip, first.Id);

        Assert.Equal(20, ScoreService.ScoreFor(trip, "sketch", "kyle"));
        Assert.Equal(30, ScoreService.ScoreFor(trip, "noodlecup", "kyle"));
        Assert.Equal(2, trip.Scores.Count);
    }

    [Fact]
    public void Undoing_something_already_gone_does_nothing()
    {
        var trip = Jake();
        ScoreService.Award(trip, "sketch", "kyle", 20, "", T0);

        ScoreService.Undo(trip, "sc-nothing");

        Assert.Single(trip.Scores);
    }

    [Fact]
    public void The_recent_list_is_newest_first_and_only_this_game()
    {
        var trip = Jake();
        ScoreService.Award(trip, "sketch", "jou", 20, "first", T0);
        ScoreService.Award(trip, "noodlecup", "ali", 10, "other game", At(5));
        ScoreService.Award(trip, "sketch", "em", 20, "second", At(10));

        var recent = ScoreService.RecentFor(trip, "sketch");

        Assert.Equal(["second", "first"], recent.Select(e => e.Note));
    }

    [Fact]
    public void Clearing_one_game_leaves_the_others_alone()
    {
        var trip = Jake();
        ScoreService.Award(trip, "sketch", "kyle", 20, "", T0);
        ScoreService.Award(trip, "noodlecup", "kyle", 30, "", T0);

        ScoreService.Clear(trip, "sketch");

        Assert.Equal(0, ScoreService.ScoreFor(trip, "sketch", "kyle"));
        Assert.Equal(30, ScoreService.ScoreFor(trip, "noodlecup", "kyle"));
    }

    [Fact]
    public void The_leader_is_the_top_scorer()
    {
        var trip = Jake();
        ScoreService.Award(trip, "sketch", "kyle", 20, "", T0);
        ScoreService.Award(trip, "sketch", "em", 40, "", T0);

        Assert.Equal("em", ScoreService.Leader(trip, "sketch")?.Id);
    }

    [Fact]
    public void Nobody_leads_an_unplayed_game_or_a_tied_one()
    {
        var trip = Jake();
        Assert.Null(ScoreService.Leader(trip, "sketch"));

        ScoreService.Award(trip, "sketch", "kyle", 20, "", T0);
        ScoreService.Award(trip, "sketch", "em", 20, "", T0);

        Assert.Null(ScoreService.Leader(trip, "sketch"));
    }
}
