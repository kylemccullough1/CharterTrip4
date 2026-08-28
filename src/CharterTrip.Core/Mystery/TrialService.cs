using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// The trials.
///
/// Five stages and no clocks. The room votes, and the moment the last living player has locked in,
/// the tally resolves on its own — so the pace is set by how long twenty-one people actually take
/// rather than by a number somebody guessed in advance.
///
/// Ties widen the cut instead of breaking it. Everyone tied at the nomination cut defends; everyone
/// tied at the conviction cut is jailed. There is no revote and no tiebreak, which means a trial has
/// no state it can hang in — the old design's worst failure was the room standing still while nobody
/// could work out whose turn it was.
/// </summary>
public static class TrialService
{
    /// <summary>Four nominees, plus anybody level with the fourth.</summary>
    public const int NomineeCount = 4;

    /// <summary>Two convictions, plus anybody level with the second.</summary>
    public const int ConvictionCount = 2;

    // ------------------------------------------------------------------------------------------
    //  Who is still playing
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Convicted, and therefore out of the voting.
    ///
    /// A ghost keeps their phone and stays at the party — they simply cannot vote or be voted for.
    /// </summary>
    public static bool IsGhost(TripData trip, string characterId) =>
        trip.Mystery.Play.ConvictedCharacterIds.Contains(characterId);

    /// <summary>
    /// Everyone who can still vote and still be voted for.
    ///
    /// Staff are excluded: Braun is dead and running the evening, and the three facilitators have no
    /// faction, no stake and no ballot. They are in the room, not in the game.
    /// </summary>
    public static IReadOnlyList<MysteryCharacter> Living(TripData trip) =>
        trip.Mystery.Story.Guests
            .Where(c => !IsGhost(trip, c.Id))
            .ToList();

    public static MysteryTrial? Current(TripData trip) =>
        trip.Mystery.Play.Trials.FirstOrDefault(t => t.Phase == trip.Mystery.Phase);

    // ------------------------------------------------------------------------------------------
    //  Running one
    // ------------------------------------------------------------------------------------------

    /// <summary>Open the ballot. Idempotent, so re-entering a trial phase does not wipe its votes.</summary>
    public static MysteryTrial OpenTrial(TripData trip, MysteryPhase phase, DateTimeOffset now)
    {
        if (trip.Mystery.Play.Trials.FirstOrDefault(t => t.Phase == phase) is { } already)
            return already;

        var trial = new MysteryTrial { Phase = phase, OpenedAt = now };
        trip.Mystery.Play.Trials.Add(trial);
        return trial;
    }

    /// <summary>
    /// Cast or change a vote.
    ///
    /// Stored per voter, so voting again replaces rather than stacks. Returns whether anything
    /// changed, which is also how the caller knows to check for everybody being in.
    /// </summary>
    public static bool CastVote(TripData trip, string voterCharacterId, string targetCharacterId, DateTimeOffset now)
    {
        var trial = Current(trip);
        if (trial is null) return false;
        if (IsGhost(trip, voterCharacterId)) return false;

        // Staff do not vote.
        if (trip.Mystery.Story.Character(voterCharacterId) is not { IsStaff: false }) return false;

        var ballots = BallotsFor(trial);
        if (ballots is null) return false;

        // Read from the same place the screen reads, so nobody can be shown as still deciding while
        // the ballot refuses them — or the other way round, which is worse.
        if (Electorate(trip).All(c => c.Id != voterCharacterId)) return false;

        if (trial.Stage == MysteryTrialStage.FinalVote)
        {
            // In the final round you may only vote for somebody standing.
            if (!trial.NomineeCharacterIds.Contains(targetCharacterId)) return false;

            // And when the whole room is standing, you may not simply vote for yourself.
            if (voterCharacterId == targetCharacterId) return false;
        }

        if (trial.Stage == MysteryTrialStage.Nominating && IsGhost(trip, targetCharacterId))
            return false;

        var existing = ballots.FirstOrDefault(v => v.VoterCharacterId == voterCharacterId);
        if (existing is not null)
        {
            if (existing.TargetCharacterId == targetCharacterId) return false;
            existing.TargetCharacterId = targetCharacterId;
            existing.At = now;
            return true;
        }

        ballots.Add(new MysteryVote
        {
            VoterCharacterId = voterCharacterId,
            TargetCharacterId = targetCharacterId,
            At = now
        });

        return true;
    }

    /// <summary>
    /// Who gets a ballot in the stage the trial is currently in.
    ///
    /// Ordinarily the people standing do not vote on their own fate. But ties widen the cut rather
    /// than breaking it, so a room that splits evenly can put <em>everybody</em> up — and then there
    /// is nobody left outside to judge them. When that happens the nominees vote on each other
    /// instead, because a trial with an empty electorate is a trial that never ends, and the room is
    /// standing there watching.
    /// </summary>
    public static IReadOnlyList<MysteryCharacter> Electorate(TripData trip)
    {
        var trial = Current(trip);
        if (trial is null) return [];

        var living = Living(trip);
        if (trial.Stage != MysteryTrialStage.FinalVote) return living;

        var outside = living.Where(c => !trial.NomineeCharacterIds.Contains(c.Id)).ToList();
        return outside.Count > 0 ? outside : living;
    }

    /// <summary>Who has not locked in yet. The screen puts typing dots beside these.</summary>
    public static IReadOnlyList<MysteryCharacter> AwaitingVote(TripData trip)
    {
        var trial = Current(trip);
        if (trial is null) return [];

        var ballots = BallotsFor(trial);
        if (ballots is null) return [];

        var voted = ballots.Select(v => v.VoterCharacterId).ToHashSet(StringComparer.Ordinal);
        return Electorate(trip).Where(c => !voted.Contains(c.Id)).ToList();
    }

    /// <summary>Everybody who can vote has. What makes the tally fire on its own.</summary>
    public static bool EveryoneHasVoted(TripData trip) => AwaitingVote(trip).Count == 0;

    /// <summary>Votes per character, highest first.</summary>
    public static IReadOnlyList<(string CharacterId, int Votes)> Tally(TripData trip)
    {
        var trial = Current(trip);
        if (trial is null) return [];

        return Tally(trial.Stage is MysteryTrialStage.Nominating or MysteryTrialStage.Tallying
            ? trial.Nominations
            : trial.FinalVotes);
    }

    /// <summary>
    /// The final round's count, whatever stage the trial is now in.
    ///
    /// The verdict screen needs it after <see cref="CloseFinalVote"/> has already moved the stage on,
    /// and "four people were taken away and the screen never said by how many votes" is exactly the
    /// moment a room stops believing the app.
    /// </summary>
    public static IReadOnlyList<(string CharacterId, int Votes)> FinalTally(TripData trip) =>
        Current(trip) is { } trial ? Tally(trial.FinalVotes) : [];

    /// <summary>How many named this person in the final round. Zero when nobody did.</summary>
    public static int FinalVotesFor(TripData trip, string characterId) =>
        FinalTally(trip).FirstOrDefault(t => t.CharacterId == characterId).Votes;

    /// <summary>
    /// Whether the cut was a tie that widened, rather than a clean two.
    ///
    /// Worth saying out loud on the verdict screen. A four-way tie convicting all four is a legal
    /// outcome and a confusing one, and the room deserves the sentence that explains it.
    /// </summary>
    public static bool ConvictedOnATie(TripData trip) =>
        Current(trip) is { } trial && trial.ConvictedCharacterIds.Count > ConvictionCount;

    private static IReadOnlyList<(string CharacterId, int Votes)> Tally(List<MysteryVote> ballots) =>
        ballots
            .GroupBy(v => v.TargetCharacterId, StringComparer.Ordinal)
            .Select(g => (CharacterId: g.Key, Votes: g.Count()))
            .OrderByDescending(x => x.Votes)
            .ThenBy(x => x.CharacterId, StringComparer.Ordinal)
            .ToList();

    private static List<MysteryVote>? BallotsFor(MysteryTrial trial) => trial.Stage switch
    {
        MysteryTrialStage.Nominating => trial.Nominations,
        MysteryTrialStage.FinalVote => trial.FinalVotes,
        _ => null
    };

    /// <summary>
    /// Close the nominations and work out who stands.
    ///
    /// Everyone tied at the fourth place goes through, so five or six nominees is a perfectly legal
    /// trial. Nobody is dropped by a coin toss.
    /// </summary>
    public static IReadOnlyList<string> CloseNominations(TripData trip)
    {
        var trial = Current(trip);
        if (trial is null || trial.Stage != MysteryTrialStage.Nominating) return [];

        trial.NomineeCharacterIds = TakeWithTies(Tally(trip), NomineeCount);
        trial.SpeakingIndex = 0;
        trial.Stage = MysteryTrialStage.Tallying;
        return trial.NomineeCharacterIds;
    }

    /// <summary>The tally animation is over; the nominees start speaking.</summary>
    public static bool BeginDefence(TripData trip)
    {
        var trial = Current(trip);
        if (trial is null || trial.Stage != MysteryTrialStage.Tallying) return false;

        trial.Stage = MysteryTrialStage.Defence;
        trial.SpeakingIndex = 0;
        return true;
    }

    /// <summary>Next nominee's turn, or false when the last one has finished.</summary>
    public static bool NextSpeaker(TripData trip)
    {
        var trial = Current(trip);
        if (trial is null || trial.Stage != MysteryTrialStage.Defence) return false;
        if (trial.SpeakingIndex + 1 >= trial.NomineeCharacterIds.Count) return false;

        trial.SpeakingIndex++;
        return true;
    }

    public static string? CurrentSpeaker(TripData trip)
    {
        var trial = Current(trip);
        if (trial is null || trial.Stage != MysteryTrialStage.Defence) return null;

        return trial.SpeakingIndex >= 0 && trial.SpeakingIndex < trial.NomineeCharacterIds.Count
            ? trial.NomineeCharacterIds[trial.SpeakingIndex]
            : null;
    }

    public static bool OpenFinalVote(TripData trip)
    {
        var trial = Current(trip);
        if (trial is null || trial.Stage != MysteryTrialStage.Defence) return false;

        trial.Stage = MysteryTrialStage.FinalVote;
        return true;
    }

    /// <summary>
    /// The verdict.
    ///
    /// Everyone tied at the second place goes down, so three or four convictions at once is a legal
    /// outcome. Which means six across the night is a floor rather than a count, and nothing
    /// downstream may assume otherwise.
    /// </summary>
    public static IReadOnlyList<string> CloseFinalVote(TripData trip, DateTimeOffset now)
    {
        var trial = Current(trip);
        if (trial is null || trial.Stage != MysteryTrialStage.FinalVote) return [];

        // A final round that produced nothing at all falls back to who the room named in the first
        // place. It should not happen — but "nobody goes to jail and the evening moves on" is the
        // one outcome a trial must never have, and the fallback is three lines.
        var tally = Tally(trip);
        if (tally.Count == 0) tally = Tally(trial.Nominations);

        trial.ConvictedCharacterIds = TakeWithTies(tally, ConvictionCount);
        trial.Stage = MysteryTrialStage.Verdict;
        trial.ClosedAt = now;
        return trial.ConvictedCharacterIds;
    }

    // ------------------------------------------------------------------------------------------
    //  Turning the cards over
    // ------------------------------------------------------------------------------------------

    /// <summary>Whether this conviction's card has been turned over yet.</summary>
    public static bool VerdictShown(TripData trip, string characterId)
    {
        var trial = Current(trip);
        if (trial is null) return false;

        var i = trial.ConvictedCharacterIds.IndexOf(characterId);
        return i >= 0 && i < trial.VerdictIndex;
    }

    /// <summary>Turn the next one over. False once they are all face up.</summary>
    public static bool RevealNextVerdict(TripData trip)
    {
        var trial = Current(trip);
        if (trial is null || trial.Stage != MysteryTrialStage.Verdict) return false;
        if (trial.VerdictIndex >= trial.ConvictedCharacterIds.Count) return false;

        trial.VerdictIndex++;
        return true;
    }

    /// <summary>Who is next under the host's thumb, for the button's label.</summary>
    public static string? NextVerdictCharacterId(TripData trip)
    {
        var trial = Current(trip);
        if (trial is null || trial.Stage != MysteryTrialStage.Verdict) return null;

        return trial.VerdictIndex < trial.ConvictedCharacterIds.Count
            ? trial.ConvictedCharacterIds[trial.VerdictIndex]
            : null;
    }

    /// <summary>
    /// The host's override, for the case nobody predicted.
    ///
    /// Every automatic path can be reached by hand from the console, which is the whole reason the
    /// evening survives a bug in front of a room.
    /// </summary>
    public static bool ForceConvict(TripData trip, IEnumerable<string> characterIds, DateTimeOffset now)
    {
        var trial = Current(trip);
        if (trial is null) return false;

        trial.ConvictedCharacterIds = characterIds.Distinct(StringComparer.Ordinal).ToList();
        trial.Stage = MysteryTrialStage.Verdict;
        trial.ClosedAt ??= now;
        return true;
    }

    /// <summary>
    /// What the room is told about a conviction: KILLER or NON-KILLER, and never the specific role.
    ///
    /// Revealing "detective" or "villager" would hand the killers a confirmed kill-list for free,
    /// and revealing "jester" ends the fun of a silent win. Two words keeps every conviction
    /// ambiguous, and is also exactly the surface a minion's shield or decoy lies about.
    /// </summary>
    public static bool ShowsAsKiller(TripData trip, string characterId)
    {
        var character = trip.Mystery.Story.Character(characterId);
        if (character is null) return false;

        var truth = character.IsKiller;

        var lie = trip.Mystery.Play.AbilityUses.FirstOrDefault(u =>
            u.TargetCharacterId == characterId && u.Mode is "shield" or "decoy");

        return lie?.Mode switch
        {
            // A killer the associates covered for reads as innocent.
            "shield" => false,

            // One of their own, sold to the room as a killer.
            "decoy" => true,

            _ => truth
        };
    }

    /// <summary>Killers convicted on ground truth. Shield and decoy fool the card, never this.</summary>
    public static int KillersConvicted(TripData trip) =>
        trip.Mystery.Play.ConvictedCharacterIds
            .Select(id => trip.Mystery.Story.Character(id))
            .Count(c => c is { IsKiller: true });

    /// <summary>
    /// Whether to skip straight to the reveal.
    ///
    /// Only on a clean sweep of all three, where there is genuinely nothing left to catch. Town
    /// winning on two does <em>not</em> end anything and the room is not told — a trial-one double
    /// hit would otherwise end a two-hour game forty minutes in and strand every jester who had not
    /// scored yet.
    /// </summary>
    public static bool ShouldEndEarly(TripData trip) =>
        KillersConvicted(trip) >= trip.Mystery.Story.Killers.Count()
        && trip.Mystery.Story.Killers.Any();

    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The top <paramref name="count"/>, plus everybody level with the last of them.
    ///
    /// The whole tie story, for both cuts. A wider result is always preferable to an arbitrary one:
    /// nobody should go to jail because their name sorts earlier.
    /// </summary>
    internal static List<string> TakeWithTies(IReadOnlyList<(string CharacterId, int Votes)> tally, int count)
    {
        if (tally.Count == 0) return [];
        if (tally.Count <= count) return tally.Select(t => t.CharacterId).ToList();

        var cutoff = tally[count - 1].Votes;
        return tally.Where(t => t.Votes >= cutoff).Select(t => t.CharacterId).ToList();
    }
}
