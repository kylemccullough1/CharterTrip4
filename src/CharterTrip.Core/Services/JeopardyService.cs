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
/// <summary>
/// The clue on the wall, whether it came out of a category or off the Final.
/// </summary>
public readonly record struct ClueInPlay(
    string Id,
    string Category,
    int Value,
    string Clue,
    string Response,
    string ClueImage,
    string ResponseImage,
    bool IsFinal);

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
        game.FinalTimerExpired = false;

        game.BuzzerCodes = trip.Teams.ToDictionary(t => t.Id, _ => NewCode(random), StringComparer.Ordinal);
        game.HostCode = NewCode(random);

        trip.Scores.RemoveAll(s => s.GameId == GameId);
    }

    /// <summary>
    /// Make sure every team and the host have a code, without touching anything else. Called when
    /// the board is opened so the join screen is usable straight away — a full Reset would clear
    /// the scores, which is not what loading a page should do.
    /// </summary>
    public static bool EnsureCodes(TripData trip, Random random)
    {
        var game = trip.Jeopardy.Game;
        var changed = false;

        foreach (var team in trip.Teams.Where(t => !game.BuzzerCodes.ContainsKey(t.Id)))
        {
            game.BuzzerCodes[team.Id] = NewCode(random);
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(game.HostCode))
        {
            game.HostCode = NewCode(random);
            changed = true;
        }

        return changed;
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

    /// <summary>
    /// The opening race settles itself the instant somebody rings in — the board is up behind it
    /// and the room can see who won, so waiting for the host to confirm a result everyone
    /// already watched happen only slowed the start down.
    ///
    /// The winning buzz is deliberately left in the list: it is what the screen reads the name
    /// and the reaction time off while the "you pick" card is up. Picking a clue clears it.
    /// </summary>
    private static void SettleBuzzOff(JeopardyGame game, string teamId)
    {
        game.PickingTeamId = teamId;
        game.BuzzersOpen = false;
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

        if (game.Buzzes.Count == 1)
        {
            // First in stops the clock; the host now judges before anyone else can ring in.
            // The final is a clue like any other, so it settles the same way.
            if (game.Phase is JeopardyPhase.Clue or JeopardyPhase.Final)
            {
                game.BuzzersOpen = false;
                game.Phase = JeopardyPhase.Judging;
            }
            // The opening race, run over the board itself. Fastest team takes the first pick.
            else if (game.Phase == JeopardyPhase.Board)
            {
                SettleBuzzOff(game, teamId);
            }
        }

        return true;
    }

    // ------------------------------------------------------------------ board

    /// <summary>
    /// Put a clue up. Picking is done from a phone as well as from the board, so this refuses
    /// anything but the moment a pick is actually due — two people tapping at once cannot
    /// otherwise replace the clue that is already on the wall.
    /// </summary>
    public static void PickClue(TripData trip, string clueId)
    {
        var game = trip.Jeopardy.Game;
        if (game.Phase != JeopardyPhase.Board || game.BuzzersOpen) return;
        if (game.UsedClueIds.Contains(clueId) || FindClue(trip, clueId) is null) return;

        game.CurrentClueId = clueId;
        game.Phase = JeopardyPhase.Clue;
        game.Buzzes.Clear();
        game.LockedOutTeamIds.Clear();
    }

    /// <summary>Right answer: the team takes the points, and the answer goes up for everyone.</summary>
    public static void JudgeCorrect(TripData trip, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;
        var clue = InPlay(trip);
        var teamId = game.LeadingBuzzTeamId;
        if (clue is not { } inPlay || teamId is null) return;

        Award(trip, teamId, inPlay.Value, $"{inPlay.Value} · correct", now);

        // Winning the final wins the game; there is no next pick to hand out.
        if (!inPlay.IsFinal) game.PickingTeamId = teamId;

        Reveal(trip, teamId);
    }

    /// <summary>
    /// Wrong answer: the value comes off, that team is out of this clue, and the buzzers reopen
    /// for everyone else. Without the deduction there is no cost to mashing the button blind.
    /// </summary>
    public static void JudgeWrong(TripData trip, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;
        var clue = InPlay(trip);
        var teamId = game.LeadingBuzzTeamId;
        if (clue is not { } inPlay || teamId is null) return;

        Award(trip, teamId, -inPlay.Value, $"{inPlay.Value} · wrong", now);

        game.LockedOutTeamIds.Add(teamId);
        game.Buzzes.Clear();

        var anyoneLeft = trip.Teams.Any(t => !game.LockedOutTeamIds.Contains(t.Id));
        if (anyoneLeft)
        {
            game.Phase = inPlay.IsFinal ? JeopardyPhase.Final : JeopardyPhase.Clue;
            game.BuzzersOpen = true;
            game.BuzzOpenedAt = now;
        }
        else
        {
            // Everyone has had a go and missed. Nobody scores, but the answer is still owed.
            Reveal(trip, winner: null);
        }
    }

    /// <summary>Nobody rang in. Put the answer up unclaimed.</summary>
    public static void NobodyGotIt(TripData trip)
    {
        if (InPlay(trip) is null) return;
        Reveal(trip, winner: null);
    }

    /// <summary>
    /// Hold the clue on screen with its answer showing. The clue is deliberately not retired
    /// here — it stays current so the answer has something to be the answer *to* — which is
    /// <see cref="Continue"/>'s job once the room has heard it.
    /// </summary>
    private static void Reveal(TripData trip, string? winner)
    {
        var game = trip.Jeopardy.Game;

        game.Phase = JeopardyPhase.Revealed;
        game.BuzzersOpen = false;
        game.FinalTimerExpired = false;
        game.Buzzes.Clear();
        game.RevealedWinnerTeamId = winner;
    }

    /// <summary>
    /// Move on from the answer. Back to the board, or into the Final titles once the board is
    /// spent, or to the winner screen if that answer was the final one.
    /// </summary>
    public static void Continue(TripData trip)
    {
        var game = trip.Jeopardy.Game;
        if (game.Phase != JeopardyPhase.Revealed) return;

        var wasFinal = game.CurrentClueId == FinalClueId;

        if (!wasFinal && game.CurrentClueId is { } id && !game.UsedClueIds.Contains(id))
            game.UsedClueIds.Add(id);

        game.CurrentClueId = null;
        game.RevealedWinnerTeamId = null;
        game.Buzzes.Clear();
        game.LockedOutTeamIds.Clear();
        game.BuzzersOpen = false;

        game.Phase = wasFinal
            ? JeopardyPhase.Finished
            : AllCluesUsed(trip) ? JeopardyPhase.FinalIntro : JeopardyPhase.Board;
    }

    // ------------------------------------------------------------------ final

    /// <summary>
    /// The id the final answers to while it is in play. It is not a real clue — it lives on the
    /// board's Final, not in a category — but giving it an id lets one set of buzz, judging and
    /// reveal rules serve both, which is the whole point of the final being a normal clue now.
    /// </summary>
    public const string FinalClueId = "final";

    /// <summary>How long the room gets to read a freshly revealed clue before buzzers go live.</summary>
    public static readonly TimeSpan ClueReadDelay = TimeSpan.FromSeconds(3);

    /// <summary>Leave the titles and put the final clue up. Buzzers open after a reading pause.</summary>
    public static void StartFinal(TripData trip)
    {
        var game = trip.Jeopardy.Game;

        game.Phase = JeopardyPhase.Final;
        game.CurrentClueId = FinalClueId;
        game.Buzzes.Clear();
        game.LockedOutTeamIds.Clear();
    }

    /// <summary>
    /// Opens the buzzers for the clue that's already on the wall, once the room's had a moment to
    /// read it. Guarded by clue id and phase so a reset or a call made during the wait can't
    /// reopen buzzers for a clue that has already moved on.
    /// </summary>
    public static void OpenBuzzers(TripData trip, string clueId, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;
        if (game.CurrentClueId != clueId) return;
        if (game.Phase is not (JeopardyPhase.Clue or JeopardyPhase.Final)) return;

        game.BuzzersOpen = true;
        game.BuzzOpenedAt = now;
    }

    /// <summary>
    /// Time's up on Final Jeopardy with nobody in: buzzers close and the room gets a beat before
    /// the host restarts it — no auto-reveal. Guarded like OpenBuzzers, so a timer that fires
    /// after the round already resolved (someone buzzed, or the host reset) does nothing.
    /// </summary>
    public static void ExpireFinalTimer(TripData trip, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;
        if (game.CurrentClueId != FinalClueId) return;
        if (game.Phase != JeopardyPhase.Final) return;
        if (!game.BuzzersOpen) return;

        game.BuzzersOpen = false;
        game.FinalTimerExpired = true;
    }

    /// <summary>Everyone's taken their shot — give the final another timed run at it.</summary>
    public static void RestartFinalTimer(TripData trip, DateTimeOffset now)
    {
        var game = trip.Jeopardy.Game;
        if (game.CurrentClueId != FinalClueId) return;
        if (game.Phase != JeopardyPhase.Final) return;
        if (!game.FinalTimerExpired) return;

        game.FinalTimerExpired = false;
        game.BuzzersOpen = true;
        game.BuzzOpenedAt = now;
    }

    /// <summary>
    /// Testing shortcut: marks every clue used and jumps straight to the Final Jeopardy titles,
    /// skipping the board entirely. Scores earned so far are untouched.
    /// </summary>
    public static void SkipToFinal(TripData trip)
    {
        var game = trip.Jeopardy.Game;
        game.UsedClueIds = trip.Jeopardy.Categories.SelectMany(c => c.Clues)
            .Where(c => !c.IsEmpty).Select(c => c.Id).ToList();
        game.CurrentClueId = null;
        game.RevealedWinnerTeamId = null;
        game.Buzzes.Clear();
        game.LockedOutTeamIds.Clear();
        game.BuzzersOpen = false;
        game.Phase = JeopardyPhase.FinalIntro;
    }

    public static void Finish(TripData trip) => trip.Jeopardy.Game.Phase = JeopardyPhase.Finished;

    // ---------------------------------------------------------------- queries

    /// <summary>
    /// Whatever is on the wall right now, flattened so the caller does not care whether it came
    /// out of a category or off the Final. Every screen and every rule reads this instead of
    /// branching on the phase, which is what keeps the final playing by the same rules as the
    /// rest of the board.
    /// </summary>
    public static ClueInPlay? InPlay(TripData trip)
    {
        var id = trip.Jeopardy.Game.CurrentClueId;
        if (id is null) return null;

        if (id == FinalClueId)
        {
            var final = trip.Jeopardy.Final;
            return new ClueInPlay(FinalClueId, final.Category, final.Value, final.Clue, final.Response,
                ClueImage: "", ResponseImage: "", IsFinal: true);
        }

        if (FindClue(trip, id) is not { } clue) return null;

        return new ClueInPlay(clue.Id, CategoryOf(trip, clue.Id)?.Name ?? "", clue.Value, clue.Clue,
            clue.Response, clue.ClueImage, clue.ResponseImage, IsFinal: false);
    }

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
