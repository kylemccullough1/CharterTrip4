using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

/// <summary>
/// The board refuses to start until every phone is in.
///
/// It used to start regardless, which meant the first anybody heard about a buzzer that never
/// worked was a team sitting silent through the opening clue — by which point that clue is gone.
/// </summary>
public class JeopardyStartTests
{
    private static TripData Trip()
    {
        var trip = new TripData
        {
            Teams =
            [
                new Team { Id = "jou", Name = "Jou" },
                new Team { Id = "ali", Name = "Ali" },
                new Team { Id = "kyle", Name = "Kyle" }
            ]
        };

        JeopardyService.EnsureCodes(trip, new Random(1));
        return trip;
    }

    [Fact]
    public void A_board_nobody_has_joined_cannot_start()
    {
        var (ready, reason) = JeopardyService.CanStart(Trip());

        Assert.False(ready);
        Assert.Contains("Jou", reason);
        Assert.Contains("Ali", reason);
        Assert.Contains("Kyle", reason);
    }

    /// <summary>The reason is read off a television, so it names who is missing.</summary>
    [Fact]
    public void It_names_the_teams_still_missing_and_nobody_else()
    {
        var trip = Trip();
        JeopardyService.RecordTeamJoin(trip, "jou");
        JeopardyService.RecordHostJoin(trip);

        var (ready, reason) = JeopardyService.CanStart(trip);

        Assert.False(ready);
        Assert.DoesNotContain("Jou", reason);
        Assert.Contains("Ali and Kyle", reason);
    }

    [Fact]
    public void The_host_counts_too_because_nobody_can_judge_without_the_answers()
    {
        var trip = Trip();
        foreach (var team in trip.Teams) JeopardyService.RecordTeamJoin(trip, team.Id);

        var (ready, reason) = JeopardyService.CanStart(trip);

        Assert.False(ready);
        Assert.Contains("answer sheet", reason);
    }

    [Fact]
    public void With_everybody_in_it_starts()
    {
        var trip = Trip();
        foreach (var team in trip.Teams) JeopardyService.RecordTeamJoin(trip, team.Id);
        JeopardyService.RecordHostJoin(trip);

        Assert.True(JeopardyService.CanStart(trip).Ready);
    }

    /// <summary>A team rejoining on a second phone is still one team present.</summary>
    [Fact]
    public void Joining_twice_counts_once()
    {
        var trip = Trip();

        Assert.True(JeopardyService.RecordTeamJoin(trip, "jou"));
        Assert.False(JeopardyService.RecordTeamJoin(trip, "jou"));
        Assert.Single(trip.Jeopardy.Game.JoinedTeamIds);
    }

    /// <summary>
    /// Reset issues new codes, so every phone in the room is suddenly holding a dead one. Leaving
    /// the joins set would let the next game start with teams that cannot buzz.
    /// </summary>
    [Fact]
    public void New_codes_mean_nobody_is_joined_any_more()
    {
        var trip = Trip();
        foreach (var team in trip.Teams) JeopardyService.RecordTeamJoin(trip, team.Id);
        JeopardyService.RecordHostJoin(trip);
        Assert.True(JeopardyService.CanStart(trip).Ready);

        JeopardyService.Reset(trip, new Random(2));

        Assert.Empty(trip.Jeopardy.Game.JoinedTeamIds);
        Assert.False(trip.Jeopardy.Game.HostJoined);
        Assert.False(JeopardyService.CanStart(trip).Ready);
    }

    [Fact]
    public void A_board_with_no_teams_says_so_rather_than_claiming_everybody_is_in()
    {
        var trip = new TripData();
        JeopardyService.RecordHostJoin(trip);

        var (ready, reason) = JeopardyService.CanStart(trip);

        Assert.False(ready);
        Assert.Contains("no teams", reason);
    }
}
