using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery.Deal;
using CharterTrip.Core.Mystery.Script;

namespace CharterTrip.Core.Mystery;

/// <summary>The outcome of closing a vote.</summary>
public enum VoteCloseKind
{
    /// <summary>Nothing to close — no votes, or the trial is not in that phase.</summary>
    NotReady,

    /// <summary>Resolved cleanly.</summary>
    Resolved,

    /// <summary>
    /// A tie at the conviction cut. <c>rounds.json</c> calls for a revote among the tied players
    /// only, and the earlier open tally as the fallback if that ties too.
    /// </summary>
    RevoteNeeded
}

/// <summary>What closing a vote decided, and who is involved.</summary>
public sealed record VoteClose(VoteCloseKind Kind, IReadOnlyList<string> CharacterIds)
{
    public static readonly VoteClose NotReady = new(VoteCloseKind.NotReady, []);
    public static VoteClose Resolved(IReadOnlyList<string> ids) => new(VoteCloseKind.Resolved, ids);
    public static VoteClose Revote(IReadOnlyList<string> ids) => new(VoteCloseKind.RevoteNeeded, ids);
}

/// <summary>Whether an ability actually fired, and what the player is told.</summary>
public sealed record AbilityResult(bool Fired, string Message)
{
    public static AbilityResult No(string why) => new(false, why);
    public static AbilityResult Yes(string message) => new(true, message);
}

/// <summary>
/// The rules of Murder at Braun Manor.
///
/// Static methods over <see cref="TripData"/>, following <c>JeopardyService</c> exactly: no state
/// of its own, nothing injected, and every method meant to be called from inside
/// <c>ITripStore.MutateAsync</c> so that a check and the change it authorises happen under the same
/// lock. That is not a style preference — the killers and the minions each have one charge shared
/// across their whole faction, and "two people press the button at the same moment" is a case that
/// will actually happen.
/// </summary>
public static class MysteryService
{
    public const string GameId = "braun-manor";

    // ---- setting up ------------------------------------------------------------------------

    /// <summary>
    /// Deal a game and lay out the clues. Replaces anything already dealt.
    ///
    /// Returns the failure reason on a roster the constraints cannot satisfy, rather than throwing
    /// or spinning — the host console shows it and the evening carries on by hand.
    /// </summary>
    public static DealFailure? DealGame(
        TripData trip, MysteryScript script, int seed, IReadOnlyList<string> personIds)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(script);

        var result = Dealer.Deal(script, personIds, seed);
        if (!result.Ok) return result.Failure;

        trip.Mystery = new MysteryState
        {
            Deal = result.Deal,
            CurrentRoundIndex = -1
        };

        trip.Mystery.Clues.AddRange(Dealer.LayOutClues(script, result.Deal!));
        return null;
    }

    /// <summary>Wipe the game back to before it was dealt. The host console's undo.</summary>
    public static void Clear(TripData trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        trip.Mystery = new MysteryState();
    }

    /// <summary>Start the evening at round one.</summary>
    public static void Start(TripData trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        if (trip.Mystery.Deal is null) return;

        trip.Mystery.Active = true;
        trip.Mystery.CurrentRoundIndex = 0;
    }

    /// <summary>
    /// Move to a round by index, clamped. The host console's force-advance, and the ordinary
    /// next-round button, are the same operation — which is the point: anything the app cannot do,
    /// a person does from that page.
    /// </summary>
    public static void GoToRound(TripData trip, MysteryScript script, int index)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(script);

        var last = script.Rounds.Rounds.Count - 1;
        trip.Mystery.CurrentRoundIndex = Math.Clamp(index, -1, last);
    }

    public static void NextRound(TripData trip, MysteryScript script) =>
        GoToRound(trip, script, trip.Mystery.CurrentRoundIndex + 1);

    public static ScriptRound? CurrentRound(TripData trip, MysteryScript script)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(script);

        var index = trip.Mystery.CurrentRoundIndex;
        return index >= 0 && index < script.Rounds.Rounds.Count ? script.Rounds.Rounds[index] : null;
    }

    // ---- who is still playing --------------------------------------------------------------

    /// <summary>A convicted player keeps their phone but stops voting. They are a ghost.</summary>
    public static bool IsGhost(TripData trip, string characterId)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return trip.Mystery.ConvictedCharacterIds.Contains(characterId, StringComparer.Ordinal);
    }

    public static IEnumerable<MysteryCastMember> Living(TripData trip)
    {
        ArgumentNullException.ThrowIfNull(trip);

        var convicted = trip.Mystery.ConvictedCharacterIds.ToHashSet(StringComparer.Ordinal);
        return trip.Mystery.Deal?.Cast.Where(c => !convicted.Contains(c.CharacterId)) ?? [];
    }

    // ---- trials ----------------------------------------------------------------------------

    /// <summary>Open a trial for the current round, or return the one already open.</summary>
    public static MysteryTrial OpenTrial(TripData trip, string roundId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trip);

        var existing = trip.Mystery.Trials.FirstOrDefault(t => t.RoundId == roundId);
        if (existing is not null) return existing;

        var trial = new MysteryTrial { RoundId = roundId, OpenedAt = now };
        trip.Mystery.Trials.Add(trial);
        return trial;
    }

    public static MysteryTrial? CurrentTrial(TripData trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return trip.Mystery.Trials.LastOrDefault(t => t.ClosedAt is null);
    }

    /// <summary>
    /// Cast or change a vote.
    ///
    /// One ballot per voter, replaced rather than added, so a player who changes their mind does
    /// not vote twice. Ghosts cannot vote and nobody can vote for a ghost. In the final vote, only
    /// non-nominees vote and only nominees can be voted for — both straight from
    /// <c>trial_procedure</c>.
    /// </summary>
    public static bool CastVote(
        TripData trip, MysteryTrial trial, string voterCharacterId, string targetCharacterId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(trial);

        if (trial.ClosedAt is not null) return false;
        if (IsGhost(trip, voterCharacterId) || IsGhost(trip, targetCharacterId)) return false;
        if (voterCharacterId == targetCharacterId) return false;

        var cast = trip.Mystery.Deal?.Cast ?? [];
        if (cast.All(c => c.CharacterId != voterCharacterId)) return false;
        if (cast.All(c => c.CharacterId != targetCharacterId)) return false;

        var ballots = trial.Phase switch
        {
            MysteryTrialPhase.OpenVote => trial.OpenVotes,
            MysteryTrialPhase.FinalVote => trial.FinalVotes,
            _ => null
        };

        if (ballots is null) return false;

        if (trial.Phase == MysteryTrialPhase.FinalVote)
        {
            // "Final vote among nominees only. Every living non-nominee votes."
            if (!trial.NomineeCharacterIds.Contains(targetCharacterId)) return false;
            if (trial.NomineeCharacterIds.Contains(voterCharacterId)) return false;
        }

        ballots.RemoveAll(v => v.VoterCharacterId == voterCharacterId);
        ballots.Add(new MysteryVote
        {
            VoterCharacterId = voterCharacterId,
            TargetCharacterId = targetCharacterId,
            At = now
        });

        return true;
    }

    /// <summary>Votes per character, highest first. What the main screen tallies.</summary>
    public static IReadOnlyList<(string CharacterId, int Votes)> Tally(IEnumerable<MysteryVote> votes)
    {
        ArgumentNullException.ThrowIfNull(votes);

        return [.. votes
            .GroupBy(v => v.TargetCharacterId, StringComparer.Ordinal)
            .Select(g => (CharacterId: g.Key, Votes: g.Count()))
            .OrderByDescending(x => x.Votes)
            .ThenBy(x => x.CharacterId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Close the open vote and nominate.
    ///
    /// "Top 4 vote-getters are nominated. Ties at the cut: all tied players nominated." So a tie
    /// for fourth place nominates five or more — which is the rule that stops a trial hanging
    /// while somebody works out who to drop.
    /// </summary>
    public static VoteClose CloseOpenVote(TripData trip, MysteryTrial trial)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(trial);

        if (trial.Phase != MysteryTrialPhase.OpenVote) return VoteClose.NotReady;
        if (trial.OpenVotes.Count == 0) return VoteClose.NotReady;

        var nominees = TakeWithTies(Tally(trial.OpenVotes), 4);

        trial.NomineeCharacterIds = [.. nominees];
        trial.Phase = MysteryTrialPhase.Defence;

        return VoteClose.Resolved(nominees);
    }

    /// <summary>Move from final words to the final ballot.</summary>
    public static bool OpenFinalVote(MysteryTrial trial)
    {
        ArgumentNullException.ThrowIfNull(trial);

        if (trial.Phase != MysteryTrialPhase.Defence) return false;

        trial.Phase = MysteryTrialPhase.FinalVote;
        return true;
    }

    /// <summary>
    /// Close the final vote and convict.
    ///
    /// "Top 2 convicted. Tie at the cut: revote between tied players only; if still tied, the
    /// earlier open-vote tally decides." A tie for second returns <see cref="VoteCloseKind.RevoteNeeded"/>
    /// with the tied players rather than guessing, because guessing here convicts somebody the room
    /// did not choose.
    /// </summary>
    public static VoteClose CloseFinalVote(TripData trip, MysteryTrial trial, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(trial);

        if (trial.Phase != MysteryTrialPhase.FinalVote) return VoteClose.NotReady;
        if (trial.FinalVotes.Count == 0) return VoteClose.NotReady;

        var tally = Tally(trial.FinalVotes);
        var top = TakeWithTies(tally, 2);

        if (top.Count > 2) return VoteClose.Revote(top);

        Convict(trip, trial, top, now);
        return VoteClose.Resolved(top);
    }

    /// <summary>
    /// Resolve a tied conviction cut using the earlier open tally, per the second half of the rule.
    ///
    /// If the open vote is also tied between them, the host picks — which is what the force
    /// controls on the host console are for. Better an explicit human decision than a coin flip
    /// the room cannot see.
    /// </summary>
    public static VoteClose ResolveTieFromOpenVote(
        TripData trip, MysteryTrial trial, IReadOnlyList<string> tied, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(tied);

        var openTally = Tally(trial.OpenVotes)
            .Where(t => tied.Contains(t.CharacterId, StringComparer.Ordinal))
            .ToList();

        var ranked = TakeWithTies(openTally, 2);
        if (ranked.Count is 0 or > 2) return VoteClose.Revote(tied);

        Convict(trip, trial, ranked, now);
        return VoteClose.Resolved(ranked);
    }

    /// <summary>The host console's override: convict exactly these people and close the trial.</summary>
    public static void ForceConvict(
        TripData trip, MysteryTrial trial, IReadOnlyList<string> characterIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(trial);

        Convict(trip, trial, characterIds, now);
    }

    private static void Convict(
        TripData trip, MysteryTrial trial, IReadOnlyList<string> characterIds, DateTimeOffset now)
    {
        trial.ConvictedCharacterIds = [.. characterIds];
        trial.Phase = MysteryTrialPhase.Revealed;
        trial.ClosedAt = now;
    }

    /// <summary>
    /// Take the top <paramref name="count"/>, keeping everybody tied at the cut.
    ///
    /// The whole reason both tie rules exist: a cut that silently dropped one of two equal
    /// vote-getters would be the room watching the app pick a victim.
    /// </summary>
    private static IReadOnlyList<string> TakeWithTies(
        IReadOnlyList<(string CharacterId, int Votes)> tally, int count)
    {
        if (tally.Count <= count) return [.. tally.Select(t => t.CharacterId)];

        var cutoff = tally[count - 1].Votes;
        return [.. tally.Where(t => t.Votes >= cutoff).Select(t => t.CharacterId)];
    }

    // ---- ending ----------------------------------------------------------------------------

    /// <summary>
    /// Whether play stops now.
    ///
    /// Town wins at 2+ killers convicted, but that is evaluated after the third trial: reaching two
    /// does NOT stop the evening. Only a clean sweep of all three ends it early, because at that
    /// point there is nothing left to catch. Firing at two would end a two-hour game after the
    /// first trial and strand every jester and Braun who had not scored.
    /// </summary>
    public static bool ShouldEndEarly(TripData trip)
    {
        ArgumentNullException.ThrowIfNull(trip);

        if (trip.Mystery.Deal is not { } deal) return false;

        var convicted = trip.Mystery.ConvictedCharacterIds.ToHashSet(StringComparer.Ordinal);
        return deal.Killers.All(k => convicted.Contains(k.CharacterId));
    }

    /// <summary>All three trials are done.</summary>
    public static bool AllTrialsComplete(TripData trip, MysteryScript script)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(script);

        var trials = script.Rounds.Trials.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        return trials.Count > 0 && trials.All(id =>
            trip.Mystery.Trials.Any(t => t.RoundId == id && t.ClosedAt is not null));
    }

    /// <summary>Work out and record who won. Idempotent.</summary>
    public static MysteryOutcome End(TripData trip, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trip);

        var outcome = Text.Compiler.Outcome(trip.Mystery, now);

        trip.Mystery.Outcome = outcome;
        trip.Mystery.Active = false;

        return outcome;
    }

    // ---- clues -----------------------------------------------------------------------------

    /// <summary>Find a clue by the token in its QR code.</summary>
    public static MysteryClue? ClueByToken(TripData trip, string? token)
    {
        ArgumentNullException.ThrowIfNull(trip);

        if (string.IsNullOrWhiteSpace(token)) return null;

        return trip.Mystery.Clues.FirstOrDefault(c =>
            string.Equals(c.Token, token, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Somebody walked to a room and scanned the card.
    ///
    /// Only the first finder is recorded — the clue is then public, and later scans are how a
    /// player re-reads it or tampers with it. Returns false if the clue was already found, which is
    /// what stops the feed filling with the same discovery.
    /// </summary>
    public static bool RecordClueFound(
        TripData trip, MysteryClue clue, string byCharacterId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(clue);

        if (clue.Found) return false;

        clue.Found = true;
        clue.FoundByCharacterId = byCharacterId;
        clue.FoundAt = now;
        return true;
    }

    /// <summary>
    /// Work somebody's belongings into a clue, or scrub it.
    ///
    /// A clue holds at most one tamper and a second attempt is refused silently — otherwise two
    /// jesters turn one card into a pile of everybody's things and the room learns nothing from it.
    /// </summary>
    public static bool TryTamper(
        MysteryClue clue, string mode, string byCharacterId, string? targetCharacterId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(clue);

        if (clue.Tamper is not null) return false;

        clue.Tamper = new MysteryTamper
        {
            Mode = mode,
            ByCharacterId = byCharacterId,
            TargetCharacterId = targetCharacterId,
            At = now
        };

        return true;
    }

    /// <summary>True if any clue has been tampered with since the given time — the round announce.</summary>
    public static bool TamperedSince(TripData trip, DateTimeOffset since)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return trip.Mystery.Clues.Any(c => c.Tamper is { } t && t.At > since);
    }

    // ---- badges ----------------------------------------------------------------------------

    /// <summary>
    /// Whose name tag this is. Badge tokens are separate from join tokens on purpose — see
    /// <see cref="MysteryCastMember.BadgeToken"/>.
    /// </summary>
    public static MysteryCastMember? ByBadge(TripData trip, string? token)
    {
        ArgumentNullException.ThrowIfNull(trip);

        if (string.IsNullOrWhiteSpace(token)) return null;

        return trip.Mystery.Deal?.Cast.FirstOrDefault(c =>
            c.BadgeToken.Length > 0 &&
            string.Equals(c.BadgeToken, token, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Log an interaction edge. Repeat scans of the same person are kept: how often two people
    /// talk is exactly what the prompt engine wants to know.
    /// </summary>
    public static void RecordScan(
        TripData trip, string byCharacterId, string metCharacterId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trip);

        if (byCharacterId == metCharacterId) return;

        trip.Mystery.Scans.Add(new MysteryScan
        {
            ByCharacterId = byCharacterId,
            MetCharacterId = metCharacterId,
            At = now
        });
    }

    /// <summary>Has this player met that one? The constraint on a detective's sync.</summary>
    public static bool HasMet(TripData trip, string byCharacterId, string otherCharacterId)
    {
        ArgumentNullException.ThrowIfNull(trip);

        return trip.Mystery.Scans.Any(s =>
            (s.ByCharacterId == byCharacterId && s.MetCharacterId == otherCharacterId) ||
            (s.ByCharacterId == otherCharacterId && s.MetCharacterId == byCharacterId));
    }

    /// <summary>Who nobody has scanned. The prompt engine's highest priority, and rightly so.</summary>
    public static IReadOnlyList<string> Underserved(TripData trip)
    {
        ArgumentNullException.ThrowIfNull(trip);

        var met = trip.Mystery.Scans
            .SelectMany(s => new[] { s.ByCharacterId, s.MetCharacterId })
            .ToHashSet(StringComparer.Ordinal);

        return [.. Living(trip).Select(c => c.CharacterId).Where(id => !met.Contains(id))];
    }

    // ---- abilities -------------------------------------------------------------------------

    /// <summary>
    /// Charges left for one player on one ability.
    ///
    /// Counted from the log of uses rather than decremented on a counter, so there is exactly one
    /// place a shared charge can be double-spent from — and that place is inside the store's lock.
    /// A shared ability pools its charges across the whole faction; a personal one is per player.
    /// </summary>
    public static int ChargesRemaining(
        TripData trip, ScriptFaction faction, ScriptAbility ability, string characterId)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(faction);
        ArgumentNullException.ThrowIfNull(ability);

        var used = trip.Mystery.AbilityUses.Count(u =>
            u.AbilityId == ability.Id &&
            (ability.Shared ? u.FactionId == faction.Id : u.ByCharacterId == characterId));

        return Math.Max(0, ability.Charges - used);
    }

    /// <summary>
    /// Whether an ability has unlocked yet.
    ///
    /// Three forms appear in <c>factions.json</c>, and none of them is a round id:
    ///
    ///   <c>after_trial_2</c>  two trials have closed
    ///   <c>round_4</c>        the round whose id starts <c>r4_</c> — so "r4_endgame"
    ///   <c>roles_drop</c>     the round that hands out roles, which is r2_investigation
    ///
    /// The <c>round_N</c> shape is the one worth knowing about: the ids are <c>r4_endgame</c> and
    /// the unlocks say <c>round_4</c>, so matching them as strings finds nothing and every ability
    /// stays locked all evening. It is also why <see cref="RoundIndexFor"/> returns null rather
    /// than a default — an unlock nothing matches has to be loud, not quietly never.
    ///
    /// Note that <c>rounds.json</c> also lists <c>unlocks</c> per round, under different names
    /// again (<c>killer_false_plant</c> for <c>evidence_hand</c>, <c>minion_blame_take</c> for
    /// <c>loyalty</c>). That is a second statement of the same schedule and it is not used here:
    /// the ability's own field wins, because it sits on the thing it governs.
    /// </summary>
    public static bool IsUnlocked(TripData trip, MysteryScript script, ScriptAbility ability)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(ability);

        var unlock = ability.Unlock;
        if (string.IsNullOrWhiteSpace(unlock)) return true;

        if (unlock.StartsWith("after_trial_", StringComparison.Ordinal)
            && int.TryParse(unlock["after_trial_".Length..], out var trialNumber))
        {
            return trip.Mystery.Trials.Count(t => t.ClosedAt is not null) >= trialNumber;
        }

        var index = RoundIndexFor(script, unlock);
        return index is not null && trip.Mystery.CurrentRoundIndex >= index;
    }

    /// <summary>
    /// The round index an unlock phrase refers to, or null if nothing matches.
    /// </summary>
    private static int? RoundIndexFor(MysteryScript script, string unlock)
    {
        // Roles land in the investigation round — it is the one whose own unlocks list the
        // detective's sync and the jester's self-frame.
        var prefix = unlock == "roles_drop"
            ? "r2_"
            : unlock.StartsWith("round_", StringComparison.Ordinal)
                ? $"r{unlock["round_".Length..]}_"
                : null;

        if (prefix is null) return null;

        for (var i = 0; i < script.Rounds.Rounds.Count; i++)
            if (script.Rounds.Rounds[i].Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return i;

        return null;
    }

    /// <summary>
    /// Unlock phrases that name a round nothing matches.
    ///
    /// Empty means every ability can eventually fire. Anything listed here is an ability that would
    /// stay locked for the whole evening with no error anywhere — the failure that hides best. The
    /// host console shows it, and a test asserts it is empty.
    /// </summary>
    public static IReadOnlyList<string> UnreachableUnlocks(MysteryScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var gaps = new List<string>();

        foreach (var faction in script.Factions.Factions)
        {
            foreach (var ability in faction.Abilities)
            {
                var unlock = ability.Unlock;
                if (string.IsNullOrWhiteSpace(unlock)) continue;
                if (unlock.StartsWith("after_trial_", StringComparison.Ordinal)) continue;

                if (RoundIndexFor(script, unlock) is null)
                    gaps.Add($"{faction.Id}.{ability.Id} → {unlock}");
            }
        }

        return gaps;
    }

    /// <summary>
    /// Spend a charge, or explain why not.
    ///
    /// Every check is here rather than in the page, and this whole method is meant to run inside
    /// <c>MutateAsync</c> — so two killers pressing at once are serialised, and the second one is
    /// told the charge is gone rather than both getting to use it.
    /// </summary>
    public static AbilityResult TryFire(
        TripData trip,
        MysteryScript script,
        string characterId,
        string abilityId,
        string? mode,
        string? targetCharacterId,
        string? targetClueId,
        string? resultMessage,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(script);

        var member = trip.Mystery.ForCharacter(characterId);
        if (member is null) return AbilityResult.No("You are not in this game.");

        if (IsGhost(trip, characterId)) return AbilityResult.No("Ghosts do not act.");

        var faction = script.Factions.ById(member.FactionId);
        var ability = faction?.Abilities.FirstOrDefault(a => a.Id == abilityId);

        if (faction is null || ability is null) return AbilityResult.No("That is not one of yours.");

        if (!IsUnlocked(trip, script, ability)) return AbilityResult.No("Not yet.");

        if (ability.HasModes && (mode is null || !ability.Modes!.ContainsKey(mode)))
            return AbilityResult.No("Choose how to use it.");

        if (ChargesRemaining(trip, faction, ability, characterId) <= 0)
            return AbilityResult.No(ability.Shared
                ? "Already spent — one of yours got there first."
                : "You have used that.");

        trip.Mystery.AbilityUses.Add(new MysteryAbilityUse
        {
            AbilityId = ability.Id,
            ByCharacterId = characterId,
            FactionId = faction.Id,
            Mode = mode,
            TargetCharacterId = targetCharacterId,
            TargetClueId = targetClueId,
            Result = resultMessage,
            At = now
        });

        return AbilityResult.Yes(resultMessage ?? ability.Name);
    }
}
