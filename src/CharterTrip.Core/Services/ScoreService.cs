using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// Awarding and taking back points, for any game that does not need a rulebook of its own.
///
/// Jeopardy has done this privately since it was built; this is the same thing with the game
/// id passed in, so the four games played on their feet did not each grow their own copy.
/// Jeopardy keeps its own — it is not worth reopening a game that works to save five lines.
///
/// Everything reads from trip.Scores rather than a tally kept alongside it, which is what
/// makes undo a deletion and makes the weekend standings incapable of disagreeing with the
/// scoreboard on the wall.
/// </summary>
public static class ScoreService
{
    /// <summary>
    /// Add one entry to the log. Points may be negative: a host correcting an over-award is
    /// the ordinary case, not an error worth refusing.
    /// </summary>
    public static ScoreEntry Award(
        TripData trip, string gameId, string teamId, int points, string note, DateTimeOffset now)
    {
        var entry = new ScoreEntry
        {
            Id = Ids.New("sc"),
            TeamId = teamId,
            GameId = gameId,
            Points = points,
            Note = note,
            At = now
        };

        trip.Scores.Add(entry);
        return entry;
    }

    /// <summary>Take one award back. Silent when it has already gone, so a double tap is harmless.</summary>
    public static void Undo(TripData trip, string entryId) =>
        trip.Scores.RemoveAll(s => s.Id == entryId);

    public static int ScoreFor(TripData trip, string gameId, string teamId) =>
        trip.Scores.Where(s => s.GameId == gameId && s.TeamId == teamId).Sum(s => s.Points);

    /// <summary>
    /// Every team and what they have earned in this one game, in the order the teams are
    /// stored — a team that has not scored is a row reading zero, not an absent one.
    /// </summary>
    public static IReadOnlyList<(Team Team, int Score)> Scoreboard(TripData trip, string gameId) =>
        trip.Teams.Select(t => (Team: t, Score: ScoreFor(trip, gameId, t.Id))).ToList();

    /// <summary>This game's awards, newest first — what the undo list is built from.</summary>
    public static IReadOnlyList<ScoreEntry> RecentFor(TripData trip, string gameId) =>
        trip.Scores.Where(s => s.GameId == gameId).OrderByDescending(s => s.At).ToList();

    /// <summary>Wipe one game's points without touching anybody else's.</summary>
    public static void Clear(TripData trip, string gameId) =>
        trip.Scores.RemoveAll(s => s.GameId == gameId);

    /// <summary>
    /// Everyone sharing the best score in this game — one name usually, two on a joint win, and
    /// none at all when nobody has scored, so a finished game with an empty board does not crown
    /// whoever happens to be stored first.
    /// </summary>
    public static IReadOnlyList<Team> Leaders(TripData trip, string gameId)
    {
        var scored = Scoreboard(trip, gameId).Where(row => row.Score > 0).ToList();
        if (scored.Count == 0) return [];

        var best = scored.Max(row => row.Score);
        return scored.Where(row => row.Score == best).Select(row => row.Team).ToList();
    }

    /// <summary>
    /// The one team winning this game, or null when nobody has scored and when the top is shared.
    /// A caller that has something to say about a joint win wants <see cref="Leaders"/> instead.
    /// </summary>
    public static Team? Leader(TripData trip, string gameId) =>
        Leaders(trip, gameId) is [var only] ? only : null;
}
