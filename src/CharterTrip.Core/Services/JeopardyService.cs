using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// The rules of the game.
///
/// Scoring deliberately has one home: the trip's ScoreEntry log, tagged with GameId "jeopardy".
/// The board reads a team's total from there rather than keeping its own tally, so the scoreboard
/// on the wall and the weekend standings can never disagree, and a reset is simply the removal of
/// those entries.
/// </summary>
public static class JeopardyService
{
    public const string GameId = "jeopardy";
    private const string CodeAlphabet = "ACDEFGHJKMNPQRTUVWXY34679";   // no O/0, I/1, S/5, B/8

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// Wipe the game back to the title card: clues unplayed, scores removed from the tally, and
    /// fresh codes so a phone left connected from last time cannot buzz into the new game.
    /// </summary>
    public static void Reset(TripData trip, Random random)
    {
        var game = trip.Jeopardy.Game;

        game.Phase = JeopardyPhase.NotStarted;
        game.UsedClueIds.Clear();
        game.CurrentClueId = null;
        game.PickingTeamId = null;
        game.Buzzes.Clear();
        game.LockedOutTeamIds.Clear();
        game.BuzzersOpen = false;
        game.FinalAnswers.Clear();
        game.FinalCorrectTeamIds.Clear();
        game.FinalRevealed = false;

        game.BuzzerCodes = trip.Teams.ToDictionary(t => t.Id, _ => NewCode(random), StringComparer.Ordinal);
        game.HostCode = NewCode(random);

        trip.Scores.RemoveAll(s => s.GameId == GameId);
    }

    private static string NewCode(Random random) =>
        new(Enumerable.Range(0, 4).Select(_ => CodeAlphabet[random.Next(CodeAlphabet.Length)]).ToArray());

    /// <summary>
    /// Open the floor to decide who picks first. Everyone buzzes; fastest wins the first pick.
    /// The television show gives it to the returning champion, which does not translate to a
    /// house game, and a race is more fun than drawing a name.
    /// </summary>
    public static void StartBuzzOff(TripData trip, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;

        game.Phase = JeopardyPhase.Board;
        game.CurrentClueId = null;
        game.PickingTeamId = null;
        game.Buzzes.Clear();
        game.LockedOutTeamIds.Clear();
        game.BuzzersOpen = true;
        game.BuzzOpenedAt = now;
    }

    /// <summary>Hand the first pick to whoever won the race, and shut the buzzers.</summary>
    public static void SettleBuzzOff(TripData trip)
    {
        var game = trip.Jeopardy.Game;
        if (game.Buzzes.Count == 0) return;

        game.PickingTeamId = game.Buzzes[0].TeamId;
        game.BuzzersOpen = false;
        game.Buzzes.Clear();
    }

    // ---------------------------------------------------------------- buzzing

    /// <summary>
    /// Record a buzz. Ordering is by arrival at the server — the closest thing to fair without
    /// synchronising clocks across five phones on house wifi.
    /// </summary>
    public static bool Buzz(TripData trip, string teamId, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;

        if (!game.BuzzersOpen) return false;
        if (game.LockedOutTeamIds.Contains(teamId)) return false;
        if (game.Buzzes.Any(b => b.TeamId == teamId)) return false;
        if (trip.Teams.All(t => t.Id != teamId)) return false;

        var opened = game.BuzzOpenedAt ?? now;
        game.Buzzes.Add(new Buzz
        {
            TeamId = teamId,
            At = now,
            Milliseconds = Math.Max(0, (int)(now - opened).TotalMilliseconds)
        });

        // First in stops the clock; the host now judges before anyone else can ring in.
        if (game.Buzzes.Count == 1 && game.Phase == JeopardyPhase.Clue)
        {
            game.BuzzersOpen = false;
            game.Phase = JeopardyPhase.Judging;
        }

        return true;
    }

    // ------------------------------------------------------------------ board

    public static void PickClue(TripData trip, string clueId, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;
        if (game.UsedClueIds.Contains(clueId) || FindClue(trip, clueId) is null) return;

        game.CurrentClueId = clueId;
        game.Phase = JeopardyPhase.Clue;
        game.Buzzes.Clear();
        game.LockedOutTeamIds.Clear();
        game.BuzzersOpen = true;
        game.BuzzOpenedAt = now;
    }

    /// <summary>Right answer: the team takes the points and picks the next clue.</summary>
    public static void JudgeCorrect(TripData trip, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;
        var clue = CurrentClue(trip);
        var teamId = game.LeadingBuzzTeamId;
        if (clue is null || teamId is null) return;

        Award(trip, teamId, clue.Value, $"{clue.Value} · correct", now);

        game.PickingTeamId = teamId;
        RetireClue(trip, clue.Id);
    }

    /// <summary>
    /// Wrong answer: the value comes off, that team is out of this clue, and the buzzers reopen
    /// for everyone else. Without the deduction there is no cost to mashing the button blind.
    /// </summary>
    public static void JudgeWrong(TripData trip, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;
        var clue = CurrentClue(trip);
        var teamId = game.LeadingBuzzTeamId;
        if (clue is null || teamId is null) return;

        Award(trip, teamId, -clue.Value, $"{clue.Value} · wrong", now);

        game.LockedOutTeamIds.Add(teamId);
        game.Buzzes.Clear();

        var anyoneLeft = trip.Teams.Any(t => !game.LockedOutTeamIds.Contains(t.Id));
        if (anyoneLeft)
        {
            game.Phase = JeopardyPhase.Clue;
            game.BuzzersOpen = true;
            game.BuzzOpenedAt = now;
        }
        else
        {
            NobodyGotIt(trip);
        }
    }

    /// <summary>Nobody rang in, or everyone has been wrong. Show the answer and move on.</summary>
    public static void NobodyGotIt(TripData trip)
    {
        var clue = CurrentClue(trip);
        if (clue is null) return;

        // The picker keeps the pick, exactly as on the show.
        RetireClue(trip, clue.Id);
    }

    private static void RetireClue(TripData trip, string clueId)
    {
        var game = trip.Jeopardy.Game;

        if (!game.UsedClueIds.Contains(clueId)) game.UsedClueIds.Add(clueId);
        game.CurrentClueId = null;
        game.Buzzes.Clear();
        game.LockedOutTeamIds.Clear();
        game.BuzzersOpen = false;
        game.Phase = AllCluesUsed(trip) ? JeopardyPhase.Final : JeopardyPhase.Board;
    }

    // ------------------------------------------------------------------ final

    public static void StartFinal(TripData trip)
    {
        var game = trip.Jeopardy.Game;
        game.Phase = JeopardyPhase.Final;
        game.BuzzersOpen = false;
        game.FinalAnswers.Clear();
        game.FinalCorrectTeamIds.Clear();
        game.FinalRevealed = false;
    }

    public static void SubmitFinalAnswer(TripData trip, string teamId, string answer)
    {
        if (trip.Jeopardy.Game.Phase != JeopardyPhase.Final) return;
        if (trip.Teams.All(t => t.Id != teamId)) return;

        trip.Jeopardy.Game.FinalAnswers[teamId] = answer.Trim();
    }

    public static void RevealFinal(TripData trip) => trip.Jeopardy.Game.FinalRevealed = true;

    /// <summary>
    /// Mark a team's final answer. Flat value, no wagering — 30 points or nothing. Marking the
    /// same team twice does not pay twice.
    /// </summary>
    public static void MarkFinal(TripData trip, string teamId, bool correct, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;
        var value = trip.Jeopardy.Final.Value;

        var alreadyCorrect = game.FinalCorrectTeamIds.Contains(teamId);
        if (correct == alreadyCorrect) return;

        if (correct)
        {
            game.FinalCorrectTeamIds.Add(teamId);
            Award(trip, teamId, value, "Final Jeopardy", now);
        }
        else
        {
            game.FinalCorrectTeamIds.Remove(teamId);
            trip.Scores.RemoveAll(s => s.GameId == GameId && s.TeamId == teamId && s.Note == "Final Jeopardy");
        }
    }

    public static void Finish(TripData trip) => trip.Jeopardy.Game.Phase = JeopardyPhase.Finished;

    // ---------------------------------------------------------------- queries

    public static JeopardyClue? CurrentClue(TripData trip) =>
        trip.Jeopardy.Game.CurrentClueId is { } id ? FindClue(trip, id) : null;

    public static JeopardyClue? FindClue(TripData trip, string clueId) =>
        trip.Jeopardy.Categories.SelectMany(c => c.Clues).FirstOrDefault(c => c.Id == clueId);

    public static JeopardyCategory? CategoryOf(TripData trip, string clueId) =>
        trip.Jeopardy.Categories.FirstOrDefault(c => c.Clues.Any(x => x.Id == clueId));

    public static bool IsUsed(TripData trip, string clueId) =>
        trip.Jeopardy.Game.UsedClueIds.Contains(clueId);

    public static bool AllCluesUsed(TripData trip) =>
        trip.Jeopardy.Categories
            .SelectMany(c => c.Clues)
            .Where(c => !c.IsEmpty)
            .All(c => trip.Jeopardy.Game.UsedClueIds.Contains(c.Id));

    /// <summary>This team's Jeopardy score, read from the one place scores live.</summary>
    public static int ScoreFor(TripData trip, string teamId) =>
        trip.Scores.Where(s => s.GameId == GameId && s.TeamId == teamId).Sum(s => s.Points);

    public static IReadOnlyList<(Team Team, int Score)> Scoreboard(TripData trip) =>
        trip.Teams.Select(t => (Team: t, Score: ScoreFor(trip, t.Id))).ToList();

    public static string? TeamForCode(TripData trip, string code) =>
        trip.Jeopardy.Game.BuzzerCodes
            .FirstOrDefault(kv => string.Equals(kv.Value, code, StringComparison.OrdinalIgnoreCase)).Key;

    public static bool IsHostCode(TripData trip, string code) =>
        !string.IsNullOrWhiteSpace(code) &&
        string.Equals(trip.Jeopardy.Game.HostCode, code, StringComparison.OrdinalIgnoreCase);

    private static void Award(TripData trip, string teamId, int points, string note, DateTimeOffset now) =>
        trip.Scores.Add(new ScoreEntry
        {
            Id = Ids.New("sc"),
            TeamId = teamId,
            GameId = GameId,
            Points = points,
            Note = note,
            At = now
        });
}
