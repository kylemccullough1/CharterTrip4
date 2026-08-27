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

    /// <summary>Clocks on the line, gun fired — the state the race spends its whole length in.</summary>
    private static RelayGame Started(TripData trip)
    {
        var game = new RelayGame();
        RelayService.Arm(game, trip.Teams);
        RelayService.StartAll(game, trip.Teams, T0);
        return game;
    }

    private static void Run(RelayGame game, string teamId, double seconds) =>
        RelayService.StopTimer(game, teamId, At(seconds));

    [Fact]
    public void Arming_the_race_puts_every_team_on_the_line()
    {
        var trip = Jake();
        var game = new RelayGame();

        RelayService.Arm(game, trip.Teams);

        Assert.Equal(PartyGamePhase.Playing, game.Phase);
        Assert.Equal(4, game.Timers.Count);
        Assert.All(game.Timers.Values, t => Assert.True(t.Armed));
        Assert.True(RelayService.NotYetRun(game, trip.Teams));
    }

    /// <summary>
    /// The whole reason there is one button rather than four: four thumbs are not simultaneous,
    /// and the gap between them is the same size as the gap between the teams.
    /// </summary>
    [Fact]
    public void The_gun_starts_every_clock_on_the_same_instant()
    {
        var trip = Jake();
        var game = new RelayGame();
        RelayService.Arm(game, trip.Teams);

        RelayService.StartAll(game, trip.Teams, T0);

        Assert.All(game.Timers.Values, t => Assert.Equal(T0, t.StartedAt));
        Assert.All(game.Timers.Values, t => Assert.True(t.Running));
        Assert.False(RelayService.NotYetRun(game, trip.Teams));
    }

    [Fact]
    public void A_clock_records_how_long_it_ran()
    {
        var trip = Jake();
        var game = Started(trip);

        Assert.True(game.Timers["kyle"].Running);

        RelayService.StopTimer(game, "kyle", At(92.5));

        Assert.False(game.Timers["kyle"].Running);
        Assert.True(game.Timers["kyle"].Stopped);
        Assert.Equal(92_500, game.Timers["kyle"].ElapsedMs);
    }

    [Fact]
    public void The_gun_does_not_restart_a_clock_that_is_already_running()
    {
        var trip = Jake();
        var game = Started(trip);

        RelayService.StartAll(game, trip.Teams, At(10));
        RelayService.StopTimer(game, "kyle", At(30));

        Assert.Equal(30_000, game.Timers["kyle"].ElapsedMs);
    }

    /// <summary>
    /// A clock stopped by mistake picks up where the race is, not where the thumb was — nobody
    /// gets seconds back for being quick on the button.
    /// </summary>
    [Fact]
    public void Un_stopping_a_clock_keeps_the_time_it_had_already_run()
    {
        var trip = Jake();
        var game = Started(trip);
        RelayService.StopTimer(game, "kyle", At(20));

        RelayService.ResumeTimer(game, "kyle");
        Assert.True(game.Timers["kyle"].Running);

        RelayService.StopTimer(game, "kyle", At(50));
        Assert.Equal(50_000, game.Timers["kyle"].ElapsedMs);
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

    // ------------------------------------------------------------------ the run-off

    [Fact]
    public void A_dead_heat_sends_exactly_those_teams_back_to_the_line()
    {
        var trip = Jake();
        var game = Started(trip);

        Run(game, "jou", 100);
        Run(game, "ali", 110);
        Run(game, "kyle", 90);
        Run(game, "em", 90);        // level with kyle

        RelayService.StartRunOff(game);

        Assert.True(game.IsRunOff);
        Assert.Equal(["em", "kyle"], game.TieBreakTeamIds.Order());
        Assert.Equal(["em", "kyle"], RelayService.InPlay(game, trip.Teams).Select(t => t.Id).Order());
        Assert.True(RelayService.NotYetRun(game, trip.Teams));
    }

    [Fact]
    public void The_run_off_is_only_over_when_both_of_them_are_in()
    {
        var trip = Jake();
        var game = Started(trip);
        Run(game, "jou", 100);
        Run(game, "ali", 110);
        Run(game, "kyle", 90);
        Run(game, "em", 90);
        RelayService.StartRunOff(game);
        RelayService.StartAll(game, trip.Teams, T0);

        Run(game, "kyle", 40);
        Assert.False(RelayService.AllStopped(game, trip.Teams));

        Run(game, "em", 45);
        Assert.True(RelayService.AllStopped(game, trip.Teams));
        Assert.Equal("kyle", RelayService.WinningTeamId(game));
    }

    [Fact]
    public void Only_a_run_off_winner_is_paid_when_the_race_was_a_dead_heat()
    {
        var trip = Jake();
        var game = Started(trip);
        Run(game, "jou", 100);
        Run(game, "ali", 110);
        Run(game, "kyle", 90);
        Run(game, "em", 90);
        RelayService.StartRunOff(game);
        RelayService.StartAll(game, trip.Teams, T0);
        Run(game, "kyle", 40);
        Run(game, "em", 45);

        RelayService.Finish(trip, game, At(200));

        var entry = Assert.Single(trip.Scores);
        Assert.Equal("kyle", entry.TeamId);
    }

    [Fact]
    public void A_run_off_is_not_set_up_when_somebody_actually_won()
    {
        var trip = Jake();
        var game = Started(trip);
        Run(game, "kyle", 90);
        Run(game, "em", 95);

        RelayService.StartRunOff(game);

        Assert.False(game.IsRunOff);
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
        Assert.Empty(game.TieBreakTeamIds);
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
