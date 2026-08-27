using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// The rules of the bee.
///
/// Scoring follows Jeopardy's discipline and has one home: the trip's ScoreEntry log, tagged
/// with GameId "spelling". Nothing here keeps a tally of its own, so the bee's result and the
/// weekend standings cannot disagree, and a reset is simply the removal of those entries.
///
/// Turns rotate by team — A, B, C, D, A… — regardless of how many survivors each team has, so
/// the last member of a team spells far more often than one of four. That is the intent: being
/// the last of your own is meant to be uncomfortable.
/// </summary>
public static class SpellingBeeService
{
    public const string GameId = "spelling";

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// Wipe the bee back to the title card and take its points off the weekend tally.
    /// </summary>
    public static void Reset(TripData trip)
    {
        trip.SpellingBee.Game = new BeeGame();
        trip.Scores.RemoveAll(s => s.GameId == GameId);
    }

    /// <summary>
    /// Put the whole field in and call the first speller.
    ///
    /// Only people on a real team can play, because the bee is won <em>for</em> a team and a
    /// speller with nowhere to send the points has no place in the rotation.
    /// </summary>
    public static void Start(TripData trip)
    {
        Reset(trip);

        var game = trip.SpellingBee.Game;
        var teamIds = trip.Teams.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        game.Survivors = trip.Roster
            .Where(p => teamIds.Contains(p.TeamId))
            .Select(p => p.Id)
            .ToList();

        BeginTurn(trip);
    }

    // ------------------------------------------------------------------ turns

    /// <summary>
    /// Call the next speller and open their turn. Does nothing if there is nobody to call or no
    /// word left to read — both are the host's problem to notice, not something to fail on.
    /// </summary>
    public static void BeginTurn(TripData trip)
    {
        var game = trip.SpellingBee.Game;

        if (NextSpeller(trip) is not { } personId) return;
        if (CurrentWord(trip) is null) return;

        game.CurrentPersonId = personId;
        game.Phase = BeePhase.Spelling;
    }

    /// <summary>
    /// Whose turn it is, advancing <see cref="BeeGame.TeamCursor"/> to the next team that still
    /// has anybody. Teams wiped out are stepped over rather than given an empty turn.
    /// </summary>
    private static string? NextSpeller(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        if (trip.Teams.Count == 0 || game.Survivors.Count == 0) return null;

        for (var step = 1; step <= trip.Teams.Count; step++)
        {
            var index = (game.TeamCursor + step) % trip.Teams.Count;
            var teamId = trip.Teams[index].Id;

            // The first survivor on this team. Survivors is a queue, so whoever sits nearest the
            // front is whoever on that team has gone longest without a turn.
            if (FirstSurvivorOn(trip, teamId) is not { } personId) continue;

            game.TeamCursor = index;
            return personId;
        }

        return null;
    }

    /// <summary>Spelled it. They go to the back of the queue and the word goes up.</summary>
    public static void JudgeCorrect(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        if (game.Phase != BeePhase.Spelling || game.CurrentPersonId is not { } personId) return;

        game.Survivors.Remove(personId);
        game.Survivors.Add(personId);

        game.LastCorrect = true;
        game.Phase = BeePhase.Revealed;
    }

    /// <summary>
    /// Missed it. Normally that is the end of their bee — but if they were the last one standing
    /// the field comes back instead, because a bee cannot end on a miss.
    /// </summary>
    public static void JudgeWrong(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        if (game.Phase != BeePhase.Spelling || game.CurrentPersonId is not { } personId) return;

        // Settled before the branch below, not after: reviving an empty field ends the bee
        // outright, and a phase assigned afterwards would quietly undo that.
        game.LastCorrect = false;
        game.Phase = BeePhase.Revealed;

        if (game.Survivors.Count <= 1)
        {
            Revive(trip);
        }
        else
        {
            game.Survivors.Remove(personId);
            game.Eliminated.Add(personId);
        }
    }

    /// <summary>
    /// The last speller standing missed, so every team gets its most recently eliminated member
    /// back and the bee carries on.
    ///
    /// Three calls are folded in here. The speller keeps their place — eliminating them would
    /// empty the field, and the rule exists precisely so the bee is not won by default. Their own
    /// team is revived along with the rest, because "each team" reads plainly and it would be a
    /// strange rule that punished the person who had earned the last spot. And a team already
    /// wiped out is revived too: the bee is the first game on Saturday, and a team with nobody
    /// left has nothing to do for the rest of it.
    /// </summary>
    private static void Revive(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        game.JustRevived = [];

        foreach (var team in trip.Teams)
        {
            // The tail of Eliminated is the most recent, so the last match is the one wanted.
            var backIn = LastEliminatedOn(trip, team.Id);
            if (backIn is null) continue;

            game.Eliminated.Remove(backIn);
            game.Survivors.Add(backIn);
            game.JustRevived.Add(backIn);
        }

        // Nobody to bring back — a solo field, or everyone revived already. Letting it stand
        // would hand the speller an unloseable turn forever, so the bee ends. It ends through
        // Finish rather than by setting the phase, so the last one in is still paid: a game
        // that is over with a winner on screen and no points in the tally is just a bug.
        if (game.JustRevived.Count == 0) Finish(trip);
    }

    /// <summary>
    /// Move on from the word. Back to the next speller, or to the winner if only one is left
    /// and they have just proved it.
    /// </summary>
    public static void Continue(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        if (game.Phase != BeePhase.Revealed) return;

        game.WordCursor++;
        game.JustRevived = [];

        // A bee is only won by spelling, never by outlasting: the last one in still has to get a
        // word right, and JudgeWrong has already refilled the field if they did not.
        if (game.Survivors.Count == 1 && game.LastCorrect)
        {
            Finish(trip);
            return;
        }

        game.CurrentPersonId = null;
        BeginTurn(trip);
    }

    /// <summary>
    /// Put someone back in who should not be out.
    ///
    /// The safety valve for a mis-tapped Wrong, which is otherwise unrecoverable — and a bee is
    /// two dozen eliminations in a row, judged live by someone also running the room. They rejoin
    /// at the back of the queue, so their team simply calls them when it comes round again.
    ///
    /// There is deliberately no manual eliminate to match it. The two mistakes are not equal:
    /// wrongly ending someone's game cannot be walked back, while wrongly sparing them means only
    /// that they spell again.
    /// </summary>
    public static void Reinstate(TripData trip, string personId)
    {
        var game = trip.SpellingBee.Game;
        if (game.Phase == BeePhase.Finished) return;
        if (!game.Eliminated.Remove(personId)) return;

        game.Survivors.Add(personId);

        // They may have been the reason a revival looked due, or the reason the bee looked won.
        game.JustRevived.Remove(personId);
    }

    /// <summary>Skip the word without settling the turn — unsayable, already used at the table, whatever.</summary>
    public static void SkipWord(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        if (game.Phase != BeePhase.Spelling) return;

        game.WordCursor++;

        // Out of words mid-turn: there is nothing to spell, so the turn cannot continue.
        if (CurrentWord(trip) is null) game.CurrentPersonId = null;
    }

    /// <summary>End the bee and send the winner's points to their team.</summary>
    private static void Finish(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        game.Phase = BeePhase.Finished;
        game.CurrentPersonId = null;

        if (Winner(trip) is not { } winner) return;

        Award(trip, winner.TeamId, trip.SpellingBee.WinnerPoints, $"{winner.Name} · last one standing");
    }

    // ---------------------------------------------------------------- queries

    /// <summary>The word on the host's card, or null once the list is spent.</summary>
    public static BeeWord? CurrentWord(TripData trip)
    {
        var words = trip.SpellingBee.Words.Where(w => !w.IsEmpty).ToList();
        var cursor = trip.SpellingBee.Game.WordCursor;

        return cursor >= 0 && cursor < words.Count ? words[cursor] : null;
    }

    /// <summary>How many words are left. The screen warns the host before this runs out.</summary>
    public static int WordsRemaining(TripData trip) =>
        Math.Max(0, trip.SpellingBee.Words.Count(w => !w.IsEmpty) - trip.SpellingBee.Game.WordCursor);

    public static RosterPerson? Speller(TripData trip) =>
        Person(trip, trip.SpellingBee.Game.CurrentPersonId);

    /// <summary>The last one standing, once the bee is over.</summary>
    public static RosterPerson? Winner(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        return game.Phase == BeePhase.Finished && game.Survivors.Count == 1
            ? Person(trip, game.Survivors[0])
            : null;
    }

    public static RosterPerson? Person(TripData trip, string? personId) =>
        personId is null ? null : trip.Roster.FirstOrDefault(p => p.Id == personId);

    /// <summary>Who this team still has in, in the order they will be called.</summary>
    public static IReadOnlyList<RosterPerson> SurvivorsOn(TripData trip, string teamId) =>
        trip.SpellingBee.Game.Survivors
            .Select(id => Person(trip, id))
            .Where(p => p is not null && p.TeamId == teamId)
            .Select(p => p!)
            .ToList();

    private static string? FirstSurvivorOn(TripData trip, string teamId) =>
        trip.SpellingBee.Game.Survivors.FirstOrDefault(id => Person(trip, id)?.TeamId == teamId);

    private static string? LastEliminatedOn(TripData trip, string teamId) =>
        trip.SpellingBee.Game.Eliminated.LastOrDefault(id => Person(trip, id)?.TeamId == teamId);

    /// <summary>This team's bee score, read from the one place scores live.</summary>
    public static int ScoreFor(TripData trip, string teamId) =>
        trip.Scores.Where(s => s.GameId == GameId && s.TeamId == teamId).Sum(s => s.Points);

    public static IReadOnlyList<(Team Team, int Score)> Scoreboard(TripData trip) =>
        trip.Teams.Select(t => (Team: t, Score: ScoreFor(trip, t.Id))).ToList();

    private static void Award(TripData trip, string teamId, int points, string note) =>
        trip.Scores.Add(new ScoreEntry
        {
            Id = Ids.New("sc"),
            TeamId = teamId,
            GameId = GameId,
            Points = points,
            Note = note,
            At = DateTimeOffset.UtcNow
        });
}
