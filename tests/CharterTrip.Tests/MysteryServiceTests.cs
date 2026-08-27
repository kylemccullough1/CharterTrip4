using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;
using CharterTrip.Core.Mystery.Script;
using CharterTrip.Infrastructure.Mystery;
using CharterTrip.Infrastructure.Seed;

namespace CharterTrip.Tests;

/// <summary>
/// The rules. The two things worth being paranoid about are here: a trial that cannot resolve, and
/// a shared charge spent twice.
/// </summary>
public class MysteryServiceTests
{
    private static readonly MysteryScript Script = ScriptLoader.Load();
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 20, 0, 0, TimeSpan.Zero);

    private static TripData Dealt(int seed = 1234)
    {
        var trip = SeedLoader.Load();
        var people = trip.Roster.Take(21).Select(p => p.Id).ToList();

        var failure = MysteryService.DealGame(trip, Script, seed, people);
        Assert.Null(failure?.Reason);

        MysteryService.Start(trip);
        return trip;
    }

    private static MysteryTrial Trial(TripData trip, string roundId = "trial_1") =>
        MysteryService.OpenTrial(trip, roundId, Now);

    /// <summary>
    /// Hand out an exact vote distribution.
    ///
    /// Voters are drawn from people who are not themselves a target, so nobody is asked to vote for
    /// themselves and the counts come out exactly as written — a test about a tie at a cut is
    /// worthless if the helper quietly delivers a different tally.
    /// </summary>
    private static void AllVoteFor(TripData trip, MysteryTrial trial, params (string Target, int Count)[] blocks)
    {
        var targets = blocks.Select(b => b.Target).ToHashSet(StringComparer.Ordinal);

        var voters = MysteryService.Living(trip)
            .Select(c => c.CharacterId)
            .Where(id => !targets.Contains(id))
            .Where(id => trial.Phase != MysteryTrialPhase.FinalVote
                         || !trial.NomineeCharacterIds.Contains(id))
            .ToList();

        var i = 0;

        foreach (var (target, count) in blocks)
        {
            for (var n = 0; n < count; n++)
            {
                Assert.True(i < voters.Count, "Not enough eligible voters for the requested tally.");
                Assert.True(MysteryService.CastVote(trip, trial, voters[i++], target, Now));
            }
        }
    }

    [Fact]
    public void Dealing_lays_out_a_game_and_nine_clues()
    {
        var trip = Dealt();

        Assert.NotNull(trip.Mystery.Deal);
        Assert.Equal(21, trip.Mystery.Deal!.Cast.Count);
        Assert.Equal(9, trip.Mystery.Clues.Count);
        Assert.True(trip.Mystery.Active);
        Assert.Equal(0, trip.Mystery.CurrentRoundIndex);
    }

    [Fact]
    public void Dealing_for_fewer_people_than_roles_leaves_the_surplus_uncast()
    {
        var trip = SeedLoader.Load();

        var failure = MysteryService.DealGame(trip, Script, 1, ["p-1", "p-2"]);

        // The game is always the same 21 characters — personIds only decides who plays them. So a
        // thin roster does not fail, it leaves empty seats for the host console to deal with.
        //
        // Which means the 17-player floor is about characters, not attendance: dropping characters
        // for a genuine no-show ("regen for 18 in the morning", TESTING.md §3) is a separate thing
        // and is not built.
        Assert.Null(failure);
        Assert.NotNull(trip.Mystery.Deal);
        Assert.Equal(21, trip.Mystery.Deal!.Cast.Count);
        Assert.Equal(2, trip.Mystery.Deal.Cast.Count(c => c.PersonId is not null));
    }

    [Fact]
    public void Every_ability_can_eventually_unlock()
    {
        // An unlock naming a round nothing matches leaves an ability dead for the whole evening
        // with no error anywhere. The round ids are r4_endgame and the unlocks say round_4, so
        // this is exactly the mismatch that hid until an ability was actually fired.
        Assert.Empty(MysteryService.UnreachableUnlocks(Script));
    }

    [Fact]
    public void Round_and_trial_unlocks_both_fire_at_the_right_moment()
    {
        var trip = Dealt();
        var jesterSelfFrame = Script.Factions.ById("jester")!.Abilities.First(a => a.Id == "self_frame");
        var killerHand = Script.Factions.ById("killer")!.Abilities.First(a => a.Id == "evidence_hand");
        var sync = Script.Factions.ById("detective")!.Abilities.First(a => a.Id == "sync");

        // r1_arrival: nothing yet.
        MysteryService.GoToRound(trip, Script, 0);
        Assert.False(MysteryService.IsUnlocked(trip, Script, jesterSelfFrame));
        Assert.False(MysteryService.IsUnlocked(trip, Script, sync));

        // round_2 is r2_investigation, at index 2 — where roles also drop.
        MysteryService.GoToRound(trip, Script, 2);
        Assert.True(MysteryService.IsUnlocked(trip, Script, jesterSelfFrame));
        Assert.True(MysteryService.IsUnlocked(trip, Script, sync));
        Assert.False(MysteryService.IsUnlocked(trip, Script, killerHand));

        // round_4 is r4_endgame, at index 6.
        MysteryService.GoToRound(trip, Script, 6);
        Assert.True(MysteryService.IsUnlocked(trip, Script, killerHand));
    }

    [Fact]
    public void Clearing_puts_it_back_to_before_anything_was_dealt()
    {
        var trip = Dealt();

        MysteryService.Clear(trip);

        Assert.Null(trip.Mystery.Deal);
        Assert.Empty(trip.Mystery.Clues);
        Assert.False(trip.Mystery.Active);
    }

    [Fact]
    public void Rounds_advance_and_clamp_at_both_ends()
    {
        var trip = Dealt();

        MysteryService.NextRound(trip, Script);
        Assert.Equal(1, trip.Mystery.CurrentRoundIndex);
        Assert.Equal(Script.Rounds.Rounds[1].Id, MysteryService.CurrentRound(trip, Script)!.Id);

        // The host console's force-advance and the ordinary next button are one operation, so
        // neither can walk off the end of the evening.
        MysteryService.GoToRound(trip, Script, 999);
        Assert.Equal(Script.Rounds.Rounds.Count - 1, trip.Mystery.CurrentRoundIndex);

        MysteryService.GoToRound(trip, Script, -50);
        Assert.Equal(-1, trip.Mystery.CurrentRoundIndex);
    }

    // ---- voting ----------------------------------------------------------------------------

    [Fact]
    public void A_second_vote_replaces_the_first_rather_than_adding_one()
    {
        var trip = Dealt();
        var trial = Trial(trip);
        var cast = trip.Mystery.Deal!.Cast;

        MysteryService.CastVote(trip, trial, cast[0].CharacterId, cast[1].CharacterId, Now);
        MysteryService.CastVote(trip, trial, cast[0].CharacterId, cast[2].CharacterId, Now);

        Assert.Single(trial.OpenVotes);
        Assert.Equal(cast[2].CharacterId, trial.OpenVotes[0].TargetCharacterId);
    }

    [Fact]
    public void Nobody_votes_for_themselves_or_for_a_ghost()
    {
        var trip = Dealt();
        var trial = Trial(trip);
        var cast = trip.Mystery.Deal!.Cast;

        Assert.False(MysteryService.CastVote(trip, trial, cast[0].CharacterId, cast[0].CharacterId, Now));

        // Convict somebody, then try to vote for them.
        var ghostTrial = MysteryService.OpenTrial(trip, "trial_0", Now);
        MysteryService.ForceConvict(trip, ghostTrial, [cast[5].CharacterId], Now);

        Assert.True(MysteryService.IsGhost(trip, cast[5].CharacterId));
        Assert.False(MysteryService.CastVote(trip, trial, cast[0].CharacterId, cast[5].CharacterId, Now));
        Assert.False(MysteryService.CastVote(trip, trial, cast[5].CharacterId, cast[0].CharacterId, Now));
    }

    [Fact]
    public void A_tie_at_the_nomination_cut_nominates_everybody_tied()
    {
        var trip = Dealt();
        var trial = Trial(trip);
        var cast = trip.Mystery.Deal!.Cast;

        // Five people on three votes each: a clean four-way cut is impossible, and the rule says
        // nominate them all rather than have the app pick who to drop.
        AllVoteFor(trip, trial,
            (cast[0].CharacterId, 3),
            (cast[1].CharacterId, 3),
            (cast[2].CharacterId, 3),
            (cast[3].CharacterId, 3),
            (cast[4].CharacterId, 3));

        var close = MysteryService.CloseOpenVote(trip, trial);

        Assert.Equal(VoteCloseKind.Resolved, close.Kind);
        Assert.Equal(5, close.CharacterIds.Count);
        Assert.Equal(MysteryTrialPhase.Defence, trial.Phase);
    }

    [Fact]
    public void A_clean_open_vote_nominates_four()
    {
        var trip = Dealt();
        var trial = Trial(trip);
        var cast = trip.Mystery.Deal!.Cast;

        AllVoteFor(trip, trial,
            (cast[0].CharacterId, 5),
            (cast[1].CharacterId, 4),
            (cast[2].CharacterId, 3),
            (cast[3].CharacterId, 2),
            (cast[4].CharacterId, 1));

        var close = MysteryService.CloseOpenVote(trip, trial);

        Assert.Equal(4, close.CharacterIds.Count);
        Assert.DoesNotContain(cast[4].CharacterId, close.CharacterIds);
    }

    [Fact]
    public void Only_non_nominees_vote_and_only_nominees_can_be_voted_for()
    {
        var trip = Dealt();
        var trial = Trial(trip);
        var cast = trip.Mystery.Deal!.Cast;

        AllVoteFor(trip, trial,
            (cast[0].CharacterId, 5), (cast[1].CharacterId, 4),
            (cast[2].CharacterId, 3), (cast[3].CharacterId, 2));

        MysteryService.CloseOpenVote(trip, trial);
        Assert.True(MysteryService.OpenFinalVote(trial));

        var nominee = trial.NomineeCharacterIds[0];
        var other = cast.First(c => !trial.NomineeCharacterIds.Contains(c.CharacterId)).CharacterId;

        // A nominee does not get to vote on their own conviction.
        Assert.False(MysteryService.CastVote(trip, trial, nominee, trial.NomineeCharacterIds[1], Now));

        // And nobody outside the nominees can be convicted at this point.
        Assert.False(MysteryService.CastVote(trip, trial, other, cast.Last().CharacterId, Now));
        Assert.True(MysteryService.CastVote(trip, trial, other, nominee, Now));
    }

    [Fact]
    public void A_clean_final_vote_convicts_two()
    {
        var trip = Dealt();
        var trial = Trial(trip);
        var cast = trip.Mystery.Deal!.Cast;

        AllVoteFor(trip, trial,
            (cast[0].CharacterId, 5), (cast[1].CharacterId, 4),
            (cast[2].CharacterId, 3), (cast[3].CharacterId, 2));

        MysteryService.CloseOpenVote(trip, trial);
        MysteryService.OpenFinalVote(trial);

        var nominees = trial.NomineeCharacterIds;
        AllVoteFor(trip, trial, (nominees[0], 5), (nominees[1], 3), (nominees[2], 1));

        var close = MysteryService.CloseFinalVote(trip, trial, Now);

        Assert.Equal(VoteCloseKind.Resolved, close.Kind);
        Assert.Equal(2, close.CharacterIds.Count);
        Assert.Equal(MysteryTrialPhase.Revealed, trial.Phase);
        Assert.NotNull(trial.ClosedAt);
        Assert.Equal(2, trip.Mystery.ConvictedCharacterIds.Count());
    }

    [Fact]
    public void A_tie_at_the_conviction_cut_asks_for_a_revote_rather_than_guessing()
    {
        var trip = Dealt();
        var trial = Trial(trip);
        var cast = trip.Mystery.Deal!.Cast;

        AllVoteFor(trip, trial,
            (cast[0].CharacterId, 5), (cast[1].CharacterId, 4),
            (cast[2].CharacterId, 3), (cast[3].CharacterId, 2));

        MysteryService.CloseOpenVote(trip, trial);
        MysteryService.OpenFinalVote(trial);

        var nominees = trial.NomineeCharacterIds;

        // One clear leader, then three tied for the second slot. Guessing here would convict
        // somebody the room did not choose.
        AllVoteFor(trip, trial, (nominees[0], 4), (nominees[1], 2), (nominees[2], 2), (nominees[3], 2));

        var close = MysteryService.CloseFinalVote(trip, trial, Now);

        Assert.Equal(VoteCloseKind.RevoteNeeded, close.Kind);
        Assert.Empty(trip.Mystery.ConvictedCharacterIds);
        Assert.NotEqual(MysteryTrialPhase.Revealed, trial.Phase);
    }

    [Fact]
    public void The_earlier_open_tally_breaks_a_tied_conviction_cut()
    {
        var trip = Dealt();
        var trial = Trial(trip);
        var cast = trip.Mystery.Deal!.Cast;

        AllVoteFor(trip, trial,
            (cast[0].CharacterId, 7), (cast[1].CharacterId, 5),
            (cast[2].CharacterId, 3), (cast[3].CharacterId, 2));

        MysteryService.CloseOpenVote(trip, trial);
        MysteryService.OpenFinalVote(trial);

        var nominees = trial.NomineeCharacterIds;
        AllVoteFor(trip, trial, (nominees[1], 2), (nominees[2], 2), (nominees[3], 2));

        var tie = MysteryService.CloseFinalVote(trip, trial, Now);
        Assert.Equal(VoteCloseKind.RevoteNeeded, tie.Kind);

        var resolved = MysteryService.ResolveTieFromOpenVote(trip, trial, tie.CharacterIds, Now);

        // Second half of the rule: the earlier open-vote tally decides between them.
        Assert.Equal(VoteCloseKind.Resolved, resolved.Kind);
        Assert.Equal(2, resolved.CharacterIds.Count);
        Assert.Equal(2, trip.Mystery.ConvictedCharacterIds.Count());
    }

    [Fact]
    public void The_host_can_convict_anybody_when_nothing_else_worked()
    {
        var trip = Dealt();
        var trial = Trial(trip);
        var cast = trip.Mystery.Deal!.Cast;

        // The safety net for the case nobody predicted. There is always a way to move the evening
        // on, which is the whole reason the host console exists.
        MysteryService.ForceConvict(trip, trial, [cast[7].CharacterId, cast[8].CharacterId], Now);

        Assert.Equal(2, trip.Mystery.ConvictedCharacterIds.Count());
        Assert.NotNull(trial.ClosedAt);
    }

    [Fact]
    public void Closing_a_vote_nobody_cast_does_nothing()
    {
        var trip = Dealt();
        var trial = Trial(trip);

        Assert.Equal(VoteCloseKind.NotReady, MysteryService.CloseOpenVote(trip, trial).Kind);
        Assert.Equal(MysteryTrialPhase.OpenVote, trial.Phase);
    }

    // ---- ending ----------------------------------------------------------------------------

    [Fact]
    public void Two_killers_convicted_wins_the_game_but_does_not_stop_it()
    {
        var trip = Dealt();
        var killers = trip.Mystery.Deal!.Killers.Select(k => k.CharacterId).ToList();

        MysteryService.ForceConvict(trip, Trial(trip), [killers[0], killers[1]], Now);

        // Town has won on the tally, and the room is not told. Play continues so that jesters and
        // Brauns still get their three trials.
        Assert.True(Core.Mystery.Text.Compiler.Outcome(trip.Mystery, Now).TownWon);
        Assert.False(MysteryService.ShouldEndEarly(trip));
    }

    [Fact]
    public void A_clean_sweep_of_all_three_ends_it_early()
    {
        var trip = Dealt();
        var killers = trip.Mystery.Deal!.Killers.Select(k => k.CharacterId).ToList();

        MysteryService.ForceConvict(trip, Trial(trip), [.. killers], Now);

        // Nothing left to catch.
        Assert.True(MysteryService.ShouldEndEarly(trip));
    }

    [Fact]
    public void Ending_records_the_outcome_and_stops_the_game()
    {
        var trip = Dealt();
        var killers = trip.Mystery.Deal!.Killers.Select(k => k.CharacterId).ToList();
        MysteryService.ForceConvict(trip, Trial(trip), [killers[0], killers[1]], Now);

        var outcome = MysteryService.End(trip, Now);

        Assert.True(outcome.TownWon);
        Assert.Equal(2, outcome.KillersConvicted);
        Assert.False(trip.Mystery.Active);
        Assert.NotNull(trip.Mystery.Outcome);
    }

    [Fact]
    public void All_trials_complete_only_when_all_three_have_closed()
    {
        var trip = Dealt();
        var cast = trip.Mystery.Deal!.Cast;

        Assert.False(MysteryService.AllTrialsComplete(trip, Script));

        var i = 0;
        foreach (var trial in Script.Rounds.Trials)
        {
            MysteryService.ForceConvict(trip, MysteryService.OpenTrial(trip, trial.Id, Now),
                [cast[i++].CharacterId], Now);
        }

        Assert.True(MysteryService.AllTrialsComplete(trip, Script));
    }

    // ---- clues and badges ------------------------------------------------------------------

    [Fact]
    public void A_clue_records_only_its_first_finder()
    {
        var trip = Dealt();
        var clue = trip.Mystery.Clues[0];
        var cast = trip.Mystery.Deal!.Cast;

        Assert.NotNull(MysteryService.ClueByToken(trip, clue.Token));
        Assert.Null(MysteryService.ClueByToken(trip, "not-a-token"));

        Assert.True(MysteryService.RecordClueFound(trip, clue, cast[0].CharacterId, Now));
        Assert.False(MysteryService.RecordClueFound(trip, clue, cast[1].CharacterId, Now));

        // Otherwise the feed fills with the same discovery every time somebody re-reads a card.
        Assert.Equal(cast[0].CharacterId, clue.FoundByCharacterId);
    }

    [Fact]
    public void A_clue_takes_one_tamper_and_refuses_the_second_silently()
    {
        var trip = Dealt();
        var clue = trip.Mystery.Clues[0];

        Assert.True(MysteryService.TryTamper(clue, "subtle", "a", "a", Now));
        Assert.False(MysteryService.TryTamper(clue, "blatant", "b", "b", Now));

        // Two jesters would otherwise turn one card into a pile of everybody's belongings.
        Assert.Equal("subtle", clue.Tamper!.Mode);
        Assert.True(MysteryService.TamperedSince(trip, Now.AddMinutes(-1)));
        Assert.False(MysteryService.TamperedSince(trip, Now.AddMinutes(1)));
    }

    [Fact]
    public void A_badge_token_is_not_a_login_token()
    {
        var trip = Dealt();

        var badges = trip.Mystery.Deal!.Cast.Select(c => c.BadgeToken).ToList();

        Assert.All(badges, b => Assert.Equal(12, b.Length));
        Assert.Equal(21, badges.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // A badge is meant to be scanned by other people — that is the mechanic. If it were the
        // join token, anybody who photographed a name tag could sign in as that player, read their
        // secrets and vote as them.
        var joinTokens = trip.Roster.Select(p => p.JoinToken).Where(t => t is not null).ToHashSet();
        Assert.All(badges, b => Assert.DoesNotContain(b, joinTokens));

        // And a badge resolves to a character rather than to an account.
        var member = trip.Mystery.Deal!.Cast[3];
        Assert.Equal(member.CharacterId, MysteryService.ByBadge(trip, member.BadgeToken)?.CharacterId);
        Assert.Null(MysteryService.ByBadge(trip, "NOTABADGE123"));
        Assert.Null(MysteryService.ByBadge(trip, null));
    }

    [Fact]
    public void Badge_scans_build_the_interaction_graph_both_ways()
    {
        var trip = Dealt();
        var cast = trip.Mystery.Deal!.Cast;

        MysteryService.RecordScan(trip, cast[0].CharacterId, cast[1].CharacterId, Now);
        MysteryService.RecordScan(trip, cast[0].CharacterId, cast[0].CharacterId, Now);   // ignored

        Assert.Single(trip.Mystery.Scans);
        Assert.True(MysteryService.HasMet(trip, cast[0].CharacterId, cast[1].CharacterId));
        Assert.True(MysteryService.HasMet(trip, cast[1].CharacterId, cast[0].CharacterId));
        Assert.False(MysteryService.HasMet(trip, cast[0].CharacterId, cast[2].CharacterId));
    }

    [Fact]
    public void Underserved_is_everybody_nobody_has_scanned()
    {
        var trip = Dealt();
        var cast = trip.Mystery.Deal!.Cast;

        Assert.Equal(21, MysteryService.Underserved(trip).Count);

        MysteryService.RecordScan(trip, cast[0].CharacterId, cast[1].CharacterId, Now);

        // The prompt engine's highest priority, and the actual failure mode of a party.
        var underserved = MysteryService.Underserved(trip);
        Assert.Equal(19, underserved.Count);
        Assert.DoesNotContain(cast[0].CharacterId, underserved);
        Assert.DoesNotContain(cast[1].CharacterId, underserved);
    }

    // ---- abilities -------------------------------------------------------------------------

    [Fact]
    public void An_ability_fires_once_and_then_reports_it_is_spent()
    {
        var trip = Dealt();
        var detective = trip.Mystery.Deal!.InFaction("detective").First().CharacterId;

        // tamper_check unlocks after_trial_1, so a trial has to have closed — moving the round on
        // is not enough, which is the distinction the unlock parser exists to keep.
        MysteryService.ForceConvict(trip, Trial(trip), [trip.Mystery.Deal!.Cast[0].CharacterId], Now);

        var first = MysteryService.TryFire(trip, Script, detective, "tamper_check", null, null, "mc-entry", "clean", Now);
        Assert.True(first.Fired, first.Message);

        var second = MysteryService.TryFire(trip, Script, detective, "tamper_check", null, null, "mc-entry", "clean", Now);
        Assert.False(second.Fired);
        Assert.Contains("used", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_shared_charge_is_spent_by_the_faction_not_the_player()
    {
        var trip = Dealt();
        MysteryService.GoToRound(trip, Script, Script.Rounds.Rounds.Count - 1);

        var killers = trip.Mystery.Deal!.InFaction("killer").Select(k => k.CharacterId).ToList();

        var first = MysteryService.TryFire(trip, Script, killers[0], "evidence_hand", "plant", killers[1], "mc-entry", null, Now);
        Assert.True(first.Fired);

        // A different killer, the same single charge. This is the case TESTING.md says will race.
        var second = MysteryService.TryFire(trip, Script, killers[1], "evidence_hand", "scrub", null, "mc-lawn", null, Now);
        Assert.False(second.Fired);
        Assert.Contains("got there first", second.Message);
    }

    [Fact]
    public void A_locked_ability_refuses_until_its_round()
    {
        var trip = Dealt();
        var killer = trip.Mystery.Deal!.InFaction("killer").First().CharacterId;

        // evidence_hand unlocks at round_4, and the game is at round 1.
        var early = MysteryService.TryFire(trip, Script, killer, "evidence_hand", "plant", null, "mc-entry", null, Now);
        Assert.False(early.Fired);
        Assert.Equal("Not yet.", early.Message);
    }

    [Fact]
    public void An_after_trial_unlock_waits_for_the_trial_to_close()
    {
        var trip = Dealt();
        var detective = trip.Mystery.Deal!.InFaction("detective").First().CharacterId;
        var ability = Script.Factions.ById("detective")!.Abilities.First(a => a.Id == "tamper_check");

        Assert.False(MysteryService.IsUnlocked(trip, Script, ability));

        MysteryService.ForceConvict(trip, Trial(trip), [trip.Mystery.Deal!.Cast[0].CharacterId], Now);

        Assert.True(MysteryService.IsUnlocked(trip, Script, ability));
        Assert.True(MysteryService.TryFire(trip, Script, detective, "tamper_check", null, null, "mc-entry", "clean", Now).Fired);
    }

    [Fact]
    public void A_mode_ability_will_not_fire_without_a_mode()
    {
        var trip = Dealt();
        MysteryService.GoToRound(trip, Script, Script.Rounds.Rounds.Count - 1);

        var jester = trip.Mystery.Deal!.InFaction("jester").First().CharacterId;

        Assert.False(MysteryService.TryFire(trip, Script, jester, "self_frame", null, null, "mc-entry", null, Now).Fired);
        Assert.False(MysteryService.TryFire(trip, Script, jester, "self_frame", "nonsense", null, "mc-entry", null, Now).Fired);
        Assert.True(MysteryService.TryFire(trip, Script, jester, "self_frame", "subtle", null, "mc-entry", null, Now).Fired);
    }

    [Fact]
    public void Nobody_uses_somebody_elses_faction_ability()
    {
        var trip = Dealt();
        MysteryService.GoToRound(trip, Script, Script.Rounds.Rounds.Count - 1);

        var villager = trip.Mystery.Deal!.InFaction("villager").First().CharacterId;

        var result = MysteryService.TryFire(trip, Script, villager, "evidence_hand", "plant", null, "mc-entry", null, Now);

        Assert.False(result.Fired);
        Assert.Contains("not one of yours", result.Message);
    }

    [Fact]
    public void Ghosts_do_not_act()
    {
        var trip = Dealt();
        MysteryService.GoToRound(trip, Script, Script.Rounds.Rounds.Count - 1);

        var detective = trip.Mystery.Deal!.InFaction("detective").First().CharacterId;
        MysteryService.ForceConvict(trip, Trial(trip), [detective], Now);

        var result = MysteryService.TryFire(trip, Script, detective, "sync", null, null, null, null, Now);

        Assert.False(result.Fired);
        Assert.Contains("Ghosts", result.Message);
    }

    /// <summary>
    /// The one that matters most: forty simultaneous presses on a single collective charge, through
    /// the real store rather than a stand-in for it.
    /// </summary>
    [Fact]
    public async Task Exactly_one_killer_wins_the_race_for_the_shared_charge()
    {
        await using var fx = new StoreFixture();

        await fx.Store.MutateAsync(t =>
        {
            MysteryService.DealGame(t, Script, 1234, [.. t.Roster.Take(21).Select(p => p.Id)]);
            MysteryService.Start(t);
            MysteryService.GoToRound(t, Script, Script.Rounds.Rounds.Count - 1);
        }, TripArea.Mystery);

        var killers = fx.Store.Current.Mystery.Deal!.InFaction("killer").Select(k => k.CharacterId).ToList();

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            fx.Store.MutateAsync(t => MysteryService.TryFire(
                t, Script, killers[i % killers.Count], "evidence_hand",
                i % 2 == 0 ? "plant" : "scrub", null, "mc-entry", null, Now),
                TripArea.Mystery)));

        Assert.Single(results, r => r.Fired);
        Assert.Equal(39, results.Count(r => !r.Fired));
        Assert.Single(fx.Store.Current.Mystery.AbilityUses);
    }
}
