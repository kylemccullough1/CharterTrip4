using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// The relay is four clocks rather than rounds. Every team runs the same legs at once, each
/// team's lead works their own clock from their own phone, and the fastest team takes the
/// whole prize — nobody else scores.
///
/// A clock records the instant it started rather than counting, so four phones watching the
/// same race all show the same time without anything being sent between them.
/// </summary>
public static class RelayService
{
    public const string GameId = "relay";

    public static void Begin(RelayGame game, IEnumerable<Team> teams)
    {
        game.Phase = PartyGamePhase.Playing;
        game.Timers = teams.ToDictionary(t => t.Id, _ => new RelayTimer(), StringComparer.Ordinal);
    }

    public static void StartTimer(RelayGame game, string teamId, DateTimeOffset now)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        var timer = Timer(game, teamId);
        if (timer.Running) return;

        timer.StartedAt = now;
        timer.ElapsedMs = null;
    }

    public static void StopTimer(RelayGame game, string teamId, DateTimeOffset now)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        var timer = Timer(game, teamId);
        if (!timer.Running) return;

        var elapsed = (now - timer.StartedAt!.Value).TotalMilliseconds;
        timer.ElapsedMs = (int)Math.Max(0, Math.Round(elapsed));
    }

    /// <summary>Put a clock back to where it was before it ran — the fix for a mis-tapped start.</summary>
    public static void ResetTimer(RelayGame game, string teamId)
    {
        if (game.Phase != PartyGamePhase.Playing) return;

        var timer = Timer(game, teamId);
        timer.StartedAt = null;
        timer.ElapsedMs = null;
    }

    /// <summary>
    /// True once every team has a finishing time. What the "end the race" button waits for —
    /// a race is not over while somebody is still running.
    /// </summary>
    public static bool AllStopped(RelayGame game, IEnumerable<Team> teams)
    {
        var all = teams.ToList();
        if (all.Count == 0) return false;

        return all.All(t => game.Timers.TryGetValue(t.Id, out var timer) && timer.Stopped);
    }

    /// <summary>The fastest finisher. Null while nobody has finished, and on a dead heat.</summary>
    public static string? WinningTeamId(RelayGame game)
    {
        var finished = game.Timers
            .Where(kv => kv.Value.Stopped)
            .OrderBy(kv => kv.Value.ElapsedMs!.Value)
            .ToList();

        if (finished.Count == 0) return null;
        if (finished.Count > 1 && finished[0].Value.ElapsedMs == finished[1].Value.ElapsedMs) return null;

        return finished[0].Key;
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
