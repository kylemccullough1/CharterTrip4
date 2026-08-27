using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// The relay is four clocks on one gun. Somebody — anybody — starts the race and every team's
/// clock starts together; each lead then stops their own from their own phone as their team
/// comes in. The fastest takes the whole prize and nobody else scores.
///
/// A clock records the instant it started rather than counting, so four phones watching the
/// same race all show the same time without anything being sent between them.
/// </summary>
public static class RelayService
{
    public const string GameId = "relay";

    /// <summary>Clocks on the line, waiting for the gun.</summary>
    public static void Arm(RelayGame game, IEnumerable<Team> teams)
    {
        game.Phase = PartyGamePhase.Playing;
        game.Timers = teams.ToDictionary(t => t.Id, _ => new RelayTimer(), StringComparer.Ordinal);
        game.TieBreakTeamIds.Clear();
    }

    /// <summary>
    /// The gun. Every team in this race starts on the same instant — which is the point of one
    /// button rather than four, since four thumbs are not simultaneous and the difference between
    /// them is the same size as the difference between the teams.
    /// </summary>
    public static void StartAll(RelayGame game, IEnumerable<Team> teams, DateTimeOffset now)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        foreach (var team in InPlay(game, teams))
        {
            var timer = Timer(game, team.Id);
            if (timer.Armed) timer.StartedAt = now;
        }
    }

    public static void StopTimer(RelayGame game, string teamId, DateTimeOffset now)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        var timer = Timer(game, teamId);
        if (!timer.Running) return;

        var elapsed = (now - timer.StartedAt!.Value).TotalMilliseconds;
        timer.ElapsedMs = (int)Math.Max(0, Math.Round(elapsed));
    }

    /// <summary>
    /// Un-stop a clock stopped by mistake. The start instant is left alone, so the clock picks up
    /// where the race actually is rather than where the thumb was — a team does not get the
    /// seconds back just because somebody was quick on the button.
    /// </summary>
    public static void ResumeTimer(RelayGame game, string teamId)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        var timer = Timer(game, teamId);
        if (timer.Stopped && timer.StartedAt is not null) timer.ElapsedMs = null;
    }

    /// <summary>Everyone still in this race: the whole field, or the run-off.</summary>
    public static IReadOnlyList<Team> InPlay(RelayGame game, IEnumerable<Team> teams) =>
        game.IsRunOff
            ? teams.Where(t => game.TieBreakTeamIds.Contains(t.Id)).ToList()
            : teams.ToList();

    /// <summary>True before the gun — every clock in play still on the line.</summary>
    public static bool NotYetRun(RelayGame game, IEnumerable<Team> teams)
    {
        var field = InPlay(game, teams);
        return field.Count > 0 && field.All(t => Timer(game, t.Id).Armed);
    }

    public static bool AnyRunning(RelayGame game, IEnumerable<Team> teams) =>
        InPlay(game, teams).Any(t => Timer(game, t.Id).Running);

    /// <summary>
    /// True once every team in this race has a finishing time. What the "end the race" button
    /// waits for — a race is not over while somebody is still running.
    /// </summary>
    public static bool AllStopped(RelayGame game, IEnumerable<Team> teams)
    {
        var field = InPlay(game, teams);
        return field.Count > 0 && field.All(t => Timer(game, t.Id).Stopped);
    }

    /// <summary>The fastest finisher. Null while nobody has finished, and on a dead heat.</summary>
    public static string? WinningTeamId(RelayGame game)
    {
        var fastest = Fastest(game);
        return fastest.Count == 1 ? fastest[0] : null;
    }

    /// <summary>Everyone sharing the quickest time — more than one means a run-off.</summary>
    public static IReadOnlyList<string> Fastest(RelayGame game)
    {
        var finished = game.Timers.Where(kv => kv.Value.Stopped).ToList();
        if (finished.Count == 0) return [];

        var best = finished.Min(kv => kv.Value.ElapsedMs!.Value);
        return finished.Where(kv => kv.Value.ElapsedMs == best).Select(kv => kv.Key).ToList();
    }

    /// <summary>
    /// Two clocks came back identical, so those teams run it again and nobody else does. The
    /// weekend scoreboard has no room for a joint first.
    /// </summary>
    public static void StartRunOff(RelayGame game)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        var tied = Fastest(game);
        if (tied.Count < 2) return;

        game.TieBreakTeamIds = tied.ToList();
        game.Timers = tied.ToDictionary(id => id, _ => new RelayTimer(), StringComparer.Ordinal);
    }

    /// <summary>
    /// What the winner earns. A team running a person short takes the larger prize — they ran
    /// the same legs with fewer people to run them.
    /// </summary>
    public static int PointsForWinner(TripData trip, RelayGame game, string teamId)
    {
        var roster = TeamService.Rosters(trip).FirstOrDefault(r => r.Team.Id == teamId);
        var size = roster?.Count ?? 0;

        return size > 0 && size <= game.SmallTeamSize ? game.SmallTeamPoints : game.WinnerPoints;
    }

    /// <summary>
    /// End the race and pay the winner. Only the winner: the sheet gives a value for first
    /// place and nothing for the rest, and inventing a sliding scale here would be inventing
    /// a rule nobody agreed to.
    /// </summary>
    public static void Finish(TripData trip, RelayGame game, DateTimeOffset now)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        if (WinningTeamId(game) is { } winner)
        {
            var elapsed = game.Timers[winner].ElapsedMs ?? 0;
            ScoreService.Award(
                trip, GameId, winner, PointsForWinner(trip, game, winner), $"First to finish · {Clock(elapsed)}", now);
        }

        game.Phase = PartyGamePhase.Finished;
    }

    public static void Reset(TripData trip, RelayGame game)
    {
        game.Phase = PartyGamePhase.NotStarted;
        game.Timers.Clear();
        game.TieBreakTeamIds.Clear();

        ScoreService.Clear(trip, GameId);
    }

    /// <summary>Milliseconds as m:ss.t — long enough to be readable, short enough to fit a card.</summary>
    public static string Clock(int milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds / 100}";
    }

    private static RelayTimer Timer(RelayGame game, string teamId)
    {
        if (game.Timers.TryGetValue(teamId, out var existing)) return existing;

        var timer = new RelayTimer();
        game.Timers[teamId] = timer;
        return timer;
    }
}
