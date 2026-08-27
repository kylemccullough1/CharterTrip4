using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class RelayServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 19, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    /// <summary>Four teams, six people each — except Em, who is a person short.</summary>
    private static TripData Jake()
    {
        var trip = new TripData
        {
            Teams =
            [
                new Team { Id = "jou",  Name = "Team Jou",  Lead = "JouJou" },
                new Team { Id = "ali",  Name = "Team Ali",  Lead = "Ali" },
                new Team { Id = "kyle", Name = "Team Kyle", Lead = "Kyle" },
                new Team { Id = "em",   Name = "Team Em",   Lead = "Emily" }
            ]
        };

        foreach (var team in trip.Teams)
        {
            var size = team.Id == "em" ? 5 : 6;
            for (var i = 0; i < size; i++)
            {
                trip.Roster.Add(new RosterPerson
                {
                    Id = $"p-{team.Id}-{i}",
                    Name = i == 0 ? team.Lead : $"{team.Id}-{i}",
                    TeamId = team.Id
                });
            }
        }

        return trip;
    }

    private static RelayGame Started(TripData trip)
    {
        var game = new RelayGame();
        RelayService.Begin(game, trip.Teams);
        return game;
    }

    private static void Run(RelayGame game, string teamId, double seconds)
    {
        RelayService.StartTimer(game, teamId, T0);
        RelayService.StopTimer(game, teamId, At(seconds));
    }

    [Fact]
    public void Beginning_the_race_gives_every_team_a_clock()
    {
        var trip = Jake();
        var game = Started(trip);

        Assert.Equal(PartyGamePhase.Playing, game.Phase);
        Assert.Equal(4, game.Timers.Count);
        Assert.All(game.Timers.Values, t => Assert.False(t.Running));
        Assert.All(game.Timers.Values, t => Assert.False(t.Stopped));
    }

    [Fact]
    public void A_clock_records_how_long_it_ran()
    {
        var trip = Jake();
        var game = Started(trip);

        RelayService.StartTimer(game, "kyle", T0);
        Assert.True(game.Timers["kyle"].Running);

        RelayService.StopTimer(game, "kyle", At(92.5));

        Assert.False(game.Timers["kyle"].Running);
        Assert.True(game.Timers["kyle"].Stopped);
        Assert.Equal(92_500, game.Timers["kyle"].ElapsedMs);
    }

    [Fact]
    public void Starting_a_running_clock_again_does_not_restart_it()
    {
        var trip = Jake();
        var game = Started(trip);

        RelayService.StartTimer(game, "kyle", T0);
        RelayService.StartTimer(game, "kyle", At(10));
        RelayService.StopTimer(game, "kyle", At(30));

        Assert.Equal(30_000, game.Timers["kyle"].ElapsedMs);
    }

    [Fact]
    public void A_mis_tapped_clock_can_be_put_back()
    {
        var trip = Jake();
        var game = Started(trip);
        RelayService.StartTimer(game, "kyle", T0);

        RelayService.ResetTimer(game, "kyle");

        Assert.False(game.Timers["kyle"].Running);
        Assert.False(game.Timers["kyle"].Stopped);
    }

    [Fact]
    public void The_race_is_not_over_while_somebody_is_still_running()
    {
        var trip = Jake();
        var game = Started(trip);

        Run(game, "jou", 100);
        Run(game, "ali", 110);
        Run(game, "kyle", 90);
        Assert.False(RelayService.AllStopped(game, trip.Teams));

        Run(game, "em", 95);
        Assert.True(RelayService.AllStopped(game, trip.Teams));
    }

    [Fact]
    public void The_fastest_team_wins()
    {
        var trip = Jake();
        var game = Started(trip);

        Run(game, "jou", 100);
        Run(game, "ali", 110);
        Run(game, "kyle", 90);
        Run(game, "em", 95);

        Assert.Equal("kyle", RelayService.WinningTeamId(game));
    }

    [Fact]
    public void Nobody_wins_an_unrun_race_or_a_dead_heat()
    {
        var trip = Jake();
        var game = Started(trip);
        Assert.Null(RelayService.WinningTeamId(game));

        Run(game, "kyle", 90);
        Run(game, "em", 90);

        Assert.Null(RelayService.WinningTeamId(game));
    }

    [Fact]
    public void A_team_running_a_person_short_takes_the_bigger_prize()
    {
        var trip = Jake();
        var game = Started(trip);

        Assert.Equal(120, RelayService.PointsForWinner(trip, game, "em"));      // five people
        Assert.Equal(100, RelayService.PointsForWinner(trip, game, "kyle"));    // six
    }

    [Fact]
    public void Only_the_winner_scores()
    {
        var trip = Jake();
        var game = Started(trip);

        Run(game, "jou", 100);
        Run(game, "ali", 110);
        Run(game, "kyle", 90);
        Run(game, "em", 95);

        RelayService.Finish(trip, game, At(200));

        Assert.Equal(PartyGamePhase.Finished, game.Phase);
        var entry = Assert.Single(trip.Scores);
        Assert.Equal("kyle", entry.TeamId);
        Assert.Equal(100, entry.Points);
        Assert.Equal(0, ScoreService.ScoreFor(trip, RelayService.GameId, "em"));
    }

    [Fact]
    public void A_short_handed_winner_is_paid_the_short_handed_rate()
    {
        var trip = Jake();
        var game = Started(trip);

        Run(game, "jou", 100);
        Run(game, "ali", 110);
        Run(game, "kyle", 105);
        Run(game, "em", 90);        // five people, and fastest

        RelayService.Finish(trip, game, At(200));

        Assert.Equal(120, ScoreService.ScoreFor(trip, RelayService.GameId, "em"));
    }

    [Fact]
    public void Reset_clears_the_clocks_and_the_points()
    {
        var trip = Jake();
        var game = Started(trip);
        Run(game, "kyle", 90);
        RelayService.Finish(trip, game, At(200));
        ScoreService.Award(trip, "sketch", "kyle", 20, "", T0);

        RelayService.Reset(trip, game);

        Assert.Equal(PartyGamePhase.NotStarted, game.Phase);
        Assert.Empty(game.Timers);
        Assert.Equal(0, ScoreService.ScoreFor(trip, RelayService.GameId, "kyle"));
        Assert.Equal(20, ScoreService.ScoreFor(trip, "sketch", "kyle"));
    }

    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(9_400, "0:09.4")]
    [InlineData(92_500, "1:32.5")]
    [InlineData(605_000, "10:05.0")]
    public void The_clock_reads_as_minutes_seconds_and_a_tenth(int ms, string expected) =>
        Assert.Equal(expected, RelayService.Clock(ms));
}
