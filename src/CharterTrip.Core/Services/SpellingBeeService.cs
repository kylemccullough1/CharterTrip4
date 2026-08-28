using CharterTrip.Core.Models;
using CharterTrip.Core.Words;

namespace CharterTrip.Core.Services;

/// <summary>
/// The rules of the bee.
///
/// Everyone stands in one shuffled row and spells in that order. Miss your word and you are out
/// for the game — unless you are the last one standing, in which case the field refills, because
/// a bee cannot be won by outlasting. Every word you get right is five points for your team,
/// banked as it happens rather than settled at the end, so a player knocked out in the third
/// round still leaves something behind.
///
/// Scoring follows Jeopardy's discipline and has one home: the trip's ScoreEntry log, tagged
/// with GameId "spelling". Nothing here keeps a tally of its own, so the bee's result and the
/// weekend standings cannot disagree, and a reset is simply the removal of those entries.
/// </summary>
public static class SpellingBeeService
{
    public const string GameId = "spelling";

    /// <summary>Same alphabet Jeopardy uses — no O/0, I/1, S/5, B/8 to misread across a room.</summary>
    private const string CodeAlphabet = "ACDEFGHJKMNPQRTUVWXY34679";

    /// <summary>Below this there is no bee, only somebody spelling at themselves.</summary>
    public const int MinimumField = 2;

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// Wipe the bee back to the title card, take its points off the weekend tally, and mint fresh
    /// codes so a phone still open from the last run cannot join the new one.
    /// </summary>
    public static void Reset(TripData trip, Random random)
    {
        ClearGame(trip);

        trip.SpellingBee.Game.GuestCode = "";
        trip.SpellingBee.Game.HostCode = "";
        EnsureCodes(trip, random);
    }

    /// <summary>
    /// Everything a reset does <em>except</em> issue new codes, which is also everything starting
    /// a bee does before it draws its first word.
    ///
    /// The split is the whole point. A reset means "forget this, we are starting over", and new
    /// codes are part of that — a phone left open from the last run should not walk into the next
    /// one. Starting means the opposite: the room has just spent five minutes scanning those
    /// codes and joining, and changing them at the moment of Start would throw every phone in
    /// the building, the host's included, back to the code box.
    /// </summary>
    private static void ClearGame(TripData trip)
    {
        var game = trip.SpellingBee.Game;

        trip.SpellingBee.Words.Clear();
        trip.Scores.RemoveAll(s => s.GameId == GameId);

        game.Phase = BeePhase.NotStarted;
        game.Order.Clear();
        game.Eliminated.Clear();
        game.Ready.Clear();
        game.CurrentPersonId = null;
        game.LastCorrect = false;
        game.JustEliminatedPersonId = null;
        game.JustRevived.Clear();
        game.RuleSlide = -1;
        game.DifficultyKey = Difficulty(trip.SpellingBee.DifficultyKey);
    }

    /// <summary>
    /// Make sure there is a guest code and a host code, without touching anything else. Called
    /// when the wall is opened so the join screen works straight away — a full Reset would clear
    /// the scores, which is not what loading a page should do.
    /// </summary>
    public static bool EnsureCodes(TripData trip, Random random)
    {
        var game = trip.SpellingBee.Game;
        var changed = false;

        if (string.IsNullOrWhiteSpace(game.GuestCode))
        {
            game.GuestCode = NewCode(random);
            changed = true;
        }

        // Drawn again rather than reused if it collides, so the two codes can never be the same
        // and hand a guest the word list.
        if (string.IsNullOrWhiteSpace(game.HostCode) || game.HostCode == game.GuestCode)
        {
            do { game.HostCode = NewCode(random); } while (game.HostCode == game.GuestCode);
            changed = true;
        }

        return changed;
    }

    private static string NewCode(Random random) =>
        new(Enumerable.Range(0, 4).Select(_ => CodeAlphabet[random.Next(CodeAlphabet.Length)]).ToArray());

    /// <summary>
    /// Somebody on a phone tapping their name, or taking it back. Only meaningful before the bee
    /// starts: once the row is dealt, joining would mean walking into a game already in progress,
    /// and the row is fixed.
    /// </summary>
    public static void SetReady(TripData trip, string personId, bool ready)
    {
        var game = trip.SpellingBee.Game;
        if (game.Phase != BeePhase.NotStarted) return;
        if (!CanPlay(trip, personId)) return;

        if (ready)
        {
            if (!game.Ready.Contains(personId)) game.Ready.Add(personId);
        }
        else
        {
            game.Ready.Remove(personId);
        }
    }

    /// <summary>
    /// Shuffle everyone who joined into a row, and call the first speller with the first word.
    ///
    /// Only people on a real team can play, because a correct word is points and a speller with
    /// nowhere to send them has no place in the row.
    /// </summary>
    public static void Start(TripData trip, Random random)
    {
        var ready = trip.SpellingBee.Game.Ready.Where(id => CanPlay(trip, id)).ToList();
        if (ready.Count < MinimumField) return;

        // Codes deliberately survive this: the room is holding them.
        ClearGame(trip);
        EnsureCodes(trip, random);

        var game = trip.SpellingBee.Game;
        game.Ready = ready;
        game.Order = WordDeck.Shuffle(ready, random);

        BeginTurn(trip, random);
    }

    /// <summary>A person may play if they exist and are on a team that exists.</summary>
    public static bool CanPlay(TripData trip, string personId) =>
        Person(trip, personId) is { } person && trip.Teams.Any(t => t.Id == person.TeamId);

    // ------------------------------------------------------------- difficulty

    /// <summary>
    /// Move the dial, up or down, one tier at a time. Takes effect on the next word drawn — never
    /// on the one somebody is standing at the microphone already spelling.
    /// </summary>
    public static void ShiftDifficulty(TripData trip, int steps)
    {
        var game = trip.SpellingBee.Game;

        game.DifficultyKey = WordDeck.Shift(Difficulty(game.DifficultyKey), steps);

        // Before the bee starts there is only one dial, and it is the one the wall's setup panel
        // is showing. Keeping them together means the host can set the opening difficulty from
        // their phone as well, which is where they are standing.
        if (game.Phase == BeePhase.NotStarted) trip.SpellingBee.DifficultyKey = game.DifficultyKey;
    }

    /// <summary>Set the opening difficulty from the wall, before anybody has spelled.</summary>
    public static void SetStartingDifficulty(TripData trip, string tierKey)
    {
        if (!WordBank.IsTier(tierKey)) return;

        trip.SpellingBee.DifficultyKey = tierKey;
        if (trip.SpellingBee.Game.Phase == BeePhase.NotStarted) trip.SpellingBee.Game.DifficultyKey = tierKey;
    }

    /// <summary>A tier key that is definitely a tier, whatever an older file or a typo says.</summary>
    private static string Difficulty(string? tierKey) =>
        tierKey is not null && WordBank.IsTier(tierKey) ? tierKey : WordDeck.DefaultDifficulty;

    // ------------------------------------------------------------------ turns

    /// <summary>
    /// Call the next speller, draw them a word, and open the turn. Does nothing if there is
    /// nobody to call.
    /// </summary>
    public static void BeginTurn(TripData trip, Random random)
    {
        var game = trip.SpellingBee.Game;

        if (NextSpeller(trip) is not { } personId) return;

        game.CurrentPersonId = personId;
        game.Phase = BeePhase.Spelling;
        DrawWord(trip, random);
    }

    /// <summary>
    /// Whose turn it is: the next person still in, walking down the row from whoever just went
    /// and wrapping at the end. The row itself never moves, so this is the only thing that has to
    /// know where we are in it.
    /// </summary>
    private static string? NextSpeller(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        if (game.Order.Count == 0) return null;

        // -1 when nobody has spelled yet, which makes the first step land on index 0.
        var from = game.CurrentPersonId is { } current ? game.Order.IndexOf(current) : -1;

        for (var step = 1; step <= game.Order.Count; step++)
        {
            var candidate = game.Order[(from + step + game.Order.Count) % game.Order.Count];
            if (!game.Eliminated.Contains(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Put a new word in play, at whatever difficulty the host has the dial set to.
    ///
    /// Everything ever drawn stays in <c>Words</c>, and everything in <c>Words</c> is off the
    /// table — so a word that was skipped is as gone as a word that was spelled. A host skips
    /// because the word was unsayable, already used at the table, or somebody's name: all
    /// reasons it should not come back an hour later.
    /// </summary>
    private static void DrawWord(TripData trip, Random random)
    {
        var bee = trip.SpellingBee;
        var difficulty = Difficulty(bee.Game.DifficultyKey);
        var used = bee.Words.Select(w => w.Word).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Three thousand eight hundred words against a bee that reads perhaps eighty, so the
        // fallback is for the impossible case rather than the unlikely one. It repeats a word
        // rather than handing the room a turn with nothing in it, because at that point the
        // choice is between a word somebody spelled an hour ago and no game.
        var word = WordDeck.Draw(difficulty, used, random)
                   ?? WordDeck.Draw(difficulty, new HashSet<string>(StringComparer.OrdinalIgnoreCase), random);

        if (word is not null) bee.Words.Add(word);
    }

    /// <summary>Spelled it. Five points for their team, and the word goes up on the wall.</summary>
    public static void JudgeCorrect(TripData trip, DateTimeOffset now)
    {
        var game = trip.SpellingBee.Game;
        if (game.Phase != BeePhase.Spelling || game.CurrentPersonId is not { } personId) return;
        if (Person(trip, personId) is not { } person) return;

        var word = CurrentWord(trip)?.Word ?? "";
        Award(trip, person.TeamId, trip.SpellingBee.PointsPerWord, $"{person.Name} · {word}", now);

        game.LastCorrect = true;
        game.JustEliminatedPersonId = null;
        game.JustRevived.Clear();
        game.Phase = BeePhase.Revealed;
    }

    /// <summary>
    /// Missed it. Normally that is the end of their bee — but if they were the last one standing
    /// the field comes back instead, because a bee cannot be won by outlasting.
    /// </summary>
    public static void JudgeWrong(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        if (game.Phase != BeePhase.Spelling || game.CurrentPersonId is not { } personId) return;

        // Settled before the branch below, not after: a revival with nobody to bring back ends
        // the bee outright, and a phase assigned afterwards would quietly undo that.
        game.LastCorrect = false;
        game.JustEliminatedPersonId = null;
        game.JustRevived.Clear();
        game.Phase = BeePhase.Revealed;

        // Counted before anything is removed, so "were they the last one standing" is asked of
        // the field they were spelling in front of.
        if (Survivors(trip).Count <= 1)
        {
            Revive(trip, personId);
            return;
        }

        if (!game.Eliminated.Contains(personId)) game.Eliminated.Add(personId);
        game.JustEliminatedPersonId = personId;
    }

    /// <summary>
    /// The last speller standing missed, so the field refills and the bee carries on.
    ///
    /// Three calls are folded in here. The speller keeps their place — eliminating them would
    /// empty the field, and the rule exists precisely so the bee is not won by default. Their own
    /// team is deliberately <em>not</em> revived: being the last of your own is meant to stay
    /// uncomfortable, and they have already earned the last spot without help. And a team already
    /// wiped out is revived along with the rest, because the bee is the first game on Saturday and
    /// a team with nobody left has nothing to do for the rest of it.
    /// </summary>
    private static void Revive(TripData trip, string survivorId)
    {
        var game = trip.SpellingBee.Game;
        var ownTeam = Person(trip, survivorId)?.TeamId;

        foreach (var team in trip.Teams)
        {
            if (string.Equals(team.Id, ownTeam, StringComparison.Ordinal)) continue;

            // The tail of Eliminated is the most recent, so the last match is the one wanted.
            if (LastEliminatedOn(trip, team.Id) is not { } backIn) continue;

            game.Eliminated.Remove(backIn);
            game.JustRevived.Add(backIn);
        }

        // Nobody to bring back — every other team is either empty or already whole, which in
        // practice means everyone still involved is on one team. Letting it stand would hand the
        // speller a turn they cannot lose and a bee that never ends, so it ends here instead.
        // Through Finish rather than by setting the phase, so the winner is worked out properly.
        if (game.JustRevived.Count == 0) Finish(trip);
    }

    /// <summary>
    /// Move on from the word. Back to the next speller, or to the winner if only one is left
    /// and they have just proved it.
    /// </summary>
    public static void Continue(TripData trip, Random random)
    {
        var game = trip.SpellingBee.Game;
        if (game.Phase != BeePhase.Revealed) return;

        game.JustEliminatedPersonId = null;
        game.JustRevived.Clear();

        // A bee is only won by spelling, never by outlasting: the last one in still has to get a
        // word right, and JudgeWrong has already refilled the field if they did not.
        if (Survivors(trip).Count == 1 && game.LastCorrect)
        {
            Finish(trip);
            return;
        }

        BeginTurn(trip, random);
    }

    /// <summary>
    /// Put someone back in who should not be out.
    ///
    /// The safety valve for a mis-tapped Wrong, which is otherwise unrecoverable — and a bee is
    /// two dozen eliminations in a row, judged live by someone also running the room. They keep
    /// their original place in the row, so the rotation simply reaches them again on its way past.
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

        if (game.JustEliminatedPersonId == personId) game.JustEliminatedPersonId = null;

        // They may have been the reason a revival looked due, or the reason the bee looked won.
        game.JustRevived.Remove(personId);
    }

    /// <summary>
    /// Swap this word for another without settling the turn — unsayable, already used at the
    /// table, whatever. The skipped one is spent: it stays in <c>Words</c> and is never drawn
    /// again for the rest of the bee.
    /// </summary>
    public static void SkipWord(TripData trip, Random random)
    {
        if (trip.SpellingBee.Game.Phase != BeePhase.Spelling) return;

        DrawWord(trip, random);
    }

    /// <summary>End the bee. The points are already banked, word by word, so there is nothing to pay.</summary>
    private static void Finish(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        game.Phase = BeePhase.Finished;
        game.JustEliminatedPersonId = null;
        game.JustRevived.Clear();

        // Kept rather than cleared: the winner is the last person to have stood at the microphone,
        // and the finished card is about them.
        game.CurrentPersonId = Survivors(trip).FirstOrDefault()?.Id;
    }

    // ------------------------------------------------------------- the rules

    /// <summary>
    /// Put one rule on the wall, or take the rules down with -1.
    ///
    /// The host talks the room through them a point at a time from their phone, which is the only
    /// reason this is game state at all: a wall showing all six at once is read by everybody at
    /// their own pace and listened to by nobody.
    /// </summary>
    public static void ShowRule(TripData trip, int index)
    {
        var game = trip.SpellingBee.Game;
        var count = Rules(trip).Count;

        // Past the last rule is how the slideshow ends: the host taps Next once more and the wall
        // goes back to the join codes, rather than the last slide needing its own button.
        game.RuleSlide = index < 0 || index >= count ? -1 : index;
    }

    /// <summary>The rules of the bee, as written on the game itself. Editable on the wall.</summary>
    public static IReadOnlyList<string> Rules(TripData trip) =>
        trip.Games.FirstOrDefault(g => g.Id == GameId)?.Rules ?? [];

    // ---------------------------------------------------------------- queries

    /// <summary>The word on the host's phone: the last one drawn, or null before the bee starts.</summary>
    public static BeeWord? CurrentWord(TripData trip) =>
        trip.SpellingBee.Words.Count > 0 ? trip.SpellingBee.Words[^1] : null;

    /// <summary>How many words this bee has got through, skips included.</summary>
    public static int WordsUsed(TripData trip) => trip.SpellingBee.Words.Count;

    public static RosterPerson? Speller(TripData trip) =>
        trip.SpellingBee.Game.Phase == BeePhase.Spelling
            ? Person(trip, trip.SpellingBee.Game.CurrentPersonId)
            : null;

    /// <summary>The last one standing, once the bee is over.</summary>
    public static RosterPerson? Winner(TripData trip) =>
        trip.SpellingBee.Game.Phase == BeePhase.Finished ? Survivors(trip).FirstOrDefault() : null;

    /// <summary>
    /// The team the bee belongs to when it is over: the winner's own.
    ///
    /// Points decide nothing here. A bee is won by the last person spelling, and their team wins
    /// it with them — a team that racked up more points on the way and then lost its last speller
    /// did not win the bee, whatever the standings say.
    /// </summary>
    public static Team? WinningTeam(TripData trip) =>
        Winner(trip) is { } winner ? trip.Teams.FirstOrDefault(t => t.Id == winner.TeamId) : null;

    public static RosterPerson? Person(TripData trip, string? personId) =>
        personId is null ? null : trip.Roster.FirstOrDefault(p => p.Id == personId);

    /// <summary>The row, in order — everyone dealt in, out or not.</summary>
    public static IReadOnlyList<RosterPerson> Field(TripData trip) =>
        trip.SpellingBee.Game.Order
            .Select(id => Person(trip, id))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

    /// <summary>Everyone still in, in row order.</summary>
    public static IReadOnlyList<RosterPerson> Survivors(TripData trip)
    {
        var game = trip.SpellingBee.Game;
        return Field(trip).Where(p => !game.Eliminated.Contains(p.Id)).ToList();
    }

    public static bool IsOut(TripData trip, string personId) =>
        trip.SpellingBee.Game.Eliminated.Contains(personId);

    /// <summary>
    /// The most recent member this team lost, or null if it has lost nobody. Eliminated is
    /// oldest-first, so the last match is the most recent — that ordering is what the revival
    /// rule reads, and it is why eliminations append rather than insert.
    /// </summary>
    private static string? LastEliminatedOn(TripData trip, string teamId) =>
        trip.SpellingBee.Game.Eliminated.LastOrDefault(id => Person(trip, id)?.TeamId == teamId);

    /// <summary>Who could still join: on a team, and not already in.</summary>
    public static IReadOnlyList<RosterPerson> Joinable(TripData trip) =>
        trip.Roster.Where(p => CanPlay(trip, p.Id)).ToList();

    public static IReadOnlyList<RosterPerson> ReadyPeople(TripData trip) =>
        trip.SpellingBee.Game.Ready
            .Select(id => Person(trip, id))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

    public static bool CanStart(TripData trip) =>
        trip.SpellingBee.Game.Phase == BeePhase.NotStarted
        && trip.SpellingBee.Game.Ready.Count(id => CanPlay(trip, id)) >= MinimumField;

    public static bool IsGuestCode(TripData trip, string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        string.Equals(trip.SpellingBee.Game.GuestCode, code, StringComparison.OrdinalIgnoreCase);

    public static bool IsHostCode(TripData trip, string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        string.Equals(trip.SpellingBee.Game.HostCode, code, StringComparison.OrdinalIgnoreCase);

    /// <summary>This team's bee score, read from the one place scores live.</summary>
    public static int ScoreFor(TripData trip, string teamId) =>
        trip.Scores.Where(s => s.GameId == GameId && s.TeamId == teamId).Sum(s => s.Points);

    public static IReadOnlyList<(Team Team, int Score)> Scoreboard(TripData trip) =>
        trip.Teams.Select(t => (Team: t, Score: ScoreFor(trip, t.Id))).ToList();

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
