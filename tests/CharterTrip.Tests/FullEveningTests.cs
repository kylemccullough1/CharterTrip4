using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;
using CharterTrip.Core.Mystery.Script;
using CharterTrip.Core.Mystery.Text;
using CharterTrip.Infrastructure.Mystery;

namespace CharterTrip.Tests;

/// <summary>
/// A whole evening, through the real store, on many seeds.
///
/// Every other test checks one rule. This one checks that they compose: deal, nine rounds, three
/// trials with everybody voting, abilities fired as they unlock, and a reveal that reads. It is the
/// closest thing to the fifteen-item scenario walk in <c>TESTING.md</c> §5 that can run in a second,
/// and it exists because the failure that matters is not a broken rule — it is a game that wedges
/// somewhere in the middle with twenty-one people watching.
/// </summary>
public class FullEveningTests
{
    private static readonly MysteryScript Script = ScriptLoader.Load();
    private static readonly DateTimeOffset Start = new(2026, 8, 29, 20, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Play one game from the deal to the reveal, voting the way a room actually would: everybody
    /// picks somebody, with the choice varying by seed so different people end up convicted.
    /// </summary>
    private static async Task<TripData> PlayAsync(int seed)
    {
        await using var fx = new StoreFixture();
        var store = fx.Store;
        var now = Start;

        // --- deal ---------------------------------------------------------------------------
        var failure = await store.MutateAsync(
            t => MysteryService.DealGame(t, Script, seed), TripArea.Mystery);

        Assert.Null(failure?.Reason);

        var rngForClaims = new Random(seed);

        // --- everybody arrives and picks up a part --------------------------------------------
        // Casting is not part of the deal any more: guests type the party code, tap their own name,
        // and are dealt whatever is still going. Twenty-one separate claims, the way the door works.
        foreach (var personId in store.Current.Roster
                     .Where(p => p.Role != TripRole.Admin)
                     .Select(p => p.Id)
                     .ToList())
        {
            var claimed = await store.MutateAsync(
                t => MysteryService.ClaimCharacter(t, personId, rngForClaims), TripArea.Mystery);

            Assert.NotNull(claimed);
        }

        Assert.Equal(0, MysteryService.SeatsLeft(store.Current));

        await store.MutateAsync(MysteryService.Start, TripArea.Mystery);

        var deal = store.Current.Mystery.Deal!;
        var rng = new Random(seed);

        // --- walk every round ----------------------------------------------------------------
        for (var index = 0; index < Script.Rounds.Rounds.Count; index++)
        {
            var round = Script.Rounds.Rounds[index];
            now = now.AddMinutes(round.Minutes);

            await store.MutateAsync(t => MysteryService.GoToRound(t, Script, index), TripArea.Mystery);

            if (!round.IsTrial)
            {
                await FindAClueAsync(store, rng, now);
                await FireWhateverIsReadyAsync(store, rng, now);
                continue;
            }

            await RunTrialAsync(store, round.Id, rng, now);

            // The early end is a clean sweep of all three, and nothing else.
            if (MysteryService.ShouldEndEarly(store.Current)) break;
        }

        // --- reveal ---------------------------------------------------------------------------
        await store.MutateAsync(t => MysteryService.End(t, now), TripArea.Mystery);
        await store.FlushAsync();

        return store.Current;
    }

    private static async Task RunTrialAsync(ITripStore store, string roundId, Random rng, DateTimeOffset now)
    {
        await store.MutateAsync(t => MysteryService.OpenTrial(t, roundId, now), TripArea.Mystery);

        var trial = store.Current.Mystery.Trials.Single(t => t.RoundId == roundId);

        // Open vote: everybody living picks somebody else, spread across a handful of targets so a
        // cut has something to bite on.
        var living = MysteryService.Living(store.Current).Select(c => c.CharacterId).ToList();
        var targets = living.OrderBy(_ => rng.Next()).Take(5).ToList();

        foreach (var voter in living)
        {
            var target = targets.First(t => t != voter);
            await store.MutateAsync(
                t => MysteryService.CastVote(t, trial, voter, target, now), TripArea.Mystery);
        }

        var nominated = await store.MutateAsync(
            t => MysteryService.CloseOpenVote(t, trial), TripArea.Mystery);

        Assert.Equal(VoteCloseKind.Resolved, nominated.Kind);
        Assert.NotEmpty(nominated.CharacterIds);

        await store.MutateAsync(_ => MysteryService.OpenFinalVote(trial), TripArea.Mystery);

        // Final vote: non-nominees only, among the nominees.
        var nominees = trial.NomineeCharacterIds;
        var electorate = MysteryService.Living(store.Current)
            .Select(c => c.CharacterId)
            .Where(id => !nominees.Contains(id))
            .ToList();

        foreach (var voter in electorate)
        {
            var target = nominees[rng.Next(nominees.Count)];
            await store.MutateAsync(
                t => MysteryService.CastVote(t, trial, voter, target, now), TripArea.Mystery);
        }

        var close = await store.MutateAsync(
            t => MysteryService.CloseFinalVote(t, trial, now), TripArea.Mystery);

        // A tie has to resolve one way or the other. The room standing still is the failure.
        if (close.Kind == VoteCloseKind.RevoteNeeded)
        {
            var tied = close.CharacterIds;
            var resolved = await store.MutateAsync(
                t => MysteryService.ResolveTieFromOpenVote(t, trial, tied, now), TripArea.Mystery);

            if (resolved.Kind != VoteCloseKind.Resolved)
            {
                // The last resort the host console exists for.
                var pick = tied.Take(2).ToList();
                await store.MutateAsync(
                    t => MysteryService.ForceConvict(t, trial, pick, now), TripArea.Mystery);
            }
        }

        Assert.NotNull(trial.ClosedAt);
        Assert.NotEmpty(trial.ConvictedCharacterIds);
    }

    private static async Task FindAClueAsync(ITripStore store, Random rng, DateTimeOffset now)
    {
        var unfound = store.Current.Mystery.Clues.Where(c => !c.Found).ToList();
        if (unfound.Count == 0) return;

        var clue = unfound[rng.Next(unfound.Count)];
        var finder = MysteryService.Living(store.Current).Select(c => c.CharacterId).First();

        await store.MutateAsync(t =>
        {
            var target = MysteryService.ClueByToken(t, clue.Token);
            if (target is not null) MysteryService.RecordClueFound(t, target, finder, now);
        }, TripArea.Mystery);
    }

    /// <summary>Fire every ability that has unlocked and still has a charge.</summary>
    private static async Task FireWhateverIsReadyAsync(ITripStore store, Random rng, DateTimeOffset now)
    {
        var deal = store.Current.Mystery.Deal!;

        foreach (var faction in Script.Factions.Factions)
        {
            foreach (var ability in faction.Abilities)
            {
                var actor = deal.InFaction(faction.Id)
                    .FirstOrDefault(m => !MysteryService.IsGhost(store.Current, m.CharacterId));

                if (actor is null) continue;

                var mode = ability.HasModes ? ability.Modes!.Keys.ElementAt(rng.Next(ability.Modes!.Count)) : null;
                var clue = store.Current.Mystery.Clues.FirstOrDefault(c => c.Tamper is null);

                // A tamper goes through the combined path, the way the phone does, so the charge
                // and the card move together.
                if (mode is not null && clue is not null)
                {
                    await store.MutateAsync(t => MysteryService.TryTamperWithAbility(
                        t, Script, actor.CharacterId, ability.Id, mode,
                        actor.CharacterId, clue.Id, now), TripArea.Mystery);
                }
                else
                {
                    await store.MutateAsync(t => MysteryService.TryFire(
                        t, Script, actor.CharacterId, ability.Id, mode,
                        actor.RivalCharacterId, null, null, now), TripArea.Mystery);
                }
            }
        }
    }

    [Fact]
    public async Task An_evening_runs_from_the_deal_to_the_reveal()
    {
        var trip = await PlayAsync(1234);
        var mystery = trip.Mystery;

        Assert.NotNull(mystery.Outcome);
        Assert.False(mystery.Active);
        Assert.NotEmpty(mystery.Trials);
        Assert.All(mystery.Trials, t => Assert.NotNull(t.ClosedAt));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(99)]
    [InlineData(1234)]
    [InlineData(20260829)]
    public async Task No_seed_wedges_the_evening(int seed)
    {
        var trip = await PlayAsync(seed);
        var mystery = trip.Mystery;
        var deal = mystery.Deal!;

        // Every trial that opened also closed, with somebody convicted. This is the wedge that
        // matters: a trial that cannot resolve leaves the room standing still.
        Assert.NotEmpty(mystery.Trials);
        Assert.All(mystery.Trials, t =>
        {
            Assert.NotNull(t.ClosedAt);
            Assert.NotEmpty(t.ConvictedCharacterIds);
        });

        // Nobody is convicted twice, and everybody convicted is somebody in the game.
        var convicted = mystery.ConvictedCharacterIds.ToList();
        Assert.Equal(convicted.Count, convicted.Distinct().Count());
        Assert.All(convicted, id => Assert.Contains(id, deal.Cast.Select(c => c.CharacterId)));

        // Six conviction slots across three trials, unless a clean sweep ended it early.
        Assert.InRange(convicted.Count, 2, 6);

        // The outcome is decided on ground truth, and it is decisive either way — Ruleset B leaves
        // no gap where nobody wins.
        var outcome = mystery.Outcome!;
        var trueCount = deal.Killers.Count(k => convicted.Contains(k.CharacterId));
        Assert.Equal(trueCount, outcome.KillersConvicted);
        Assert.Equal(trueCount >= 2, outcome.TownWon);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    [InlineData(77)]
    public async Task The_reveal_reads_without_a_hole_in_it(int seed)
    {
        var trip = await PlayAsync(seed);

        var paragraphs = Compiler.Endgame(Script, trip.Mystery);

        Assert.NotEmpty(paragraphs);
        foreach (var paragraph in paragraphs)
        {
            Assert.DoesNotContain("{", paragraph);
            Assert.False(string.IsNullOrWhiteSpace(paragraph));
        }

        // Every red herring is cleared by name, which is what makes burning one feel fair.
        foreach (var herring in trip.Mystery.Deal!.Herrings)
        {
            var truth = Script.CharacterById(herring.CharacterId)!.HerringTruth;
            Assert.Contains(paragraphs, p => p.Contains(truth));
        }
    }

    [Fact]
    public async Task Every_shared_charge_is_spent_at_most_once_across_a_whole_evening()
    {
        var trip = await PlayAsync(55);

        foreach (var faction in Script.Factions.Factions)
        {
            foreach (var ability in faction.Abilities.Where(a => a.Shared))
            {
                var used = trip.Mystery.AbilityUses.Count(u =>
                    u.AbilityId == ability.Id && u.FactionId == faction.Id);

                Assert.True(used <= ability.Charges,
                    $"{faction.Id}.{ability.Id} spent {used} of {ability.Charges}.");
            }
        }
    }

    [Fact]
    public async Task A_spent_tamper_charge_always_matches_a_tampered_card()
    {
        var trip = await PlayAsync(88);

        // The invariant the combined TryTamperWithAbility exists to hold. Spending a charge on a
        // card somebody already got to, or marking a card with nothing left to spend, both put the
        // game and what the room can see out of step.
        var chargesSpentOnClues = trip.Mystery.AbilityUses
            .Where(u => u.TargetClueId is not null && u.Mode is not null)
            .ToList();

        var tamperedClues = trip.Mystery.Clues.Where(c => c.Tamper is not null).ToList();

        Assert.Equal(tamperedClues.Count, chargesSpentOnClues.Count);
        Assert.Equal(
            chargesSpentOnClues.Select(u => u.TargetClueId).Order(),
            tamperedClues.Select(c => c.Id).Order());

        // And each of those cards names the person who spent the charge on it.
        foreach (var clue in tamperedClues)
        {
            var use = chargesSpentOnClues.Single(u => u.TargetClueId == clue.Id);
            Assert.Equal(use.ByCharacterId, clue.Tamper!.ByCharacterId);
            Assert.Equal(use.Mode, clue.Tamper.Mode);
        }
    }

    [Fact]
    public async Task Every_briefing_still_composes_after_a_full_evening_of_tampering()
    {
        var trip = await PlayAsync(4321);
        var deal = trip.Mystery.Deal!;

        foreach (var member in deal.Cast)
        {
            var briefing = Compiler.BriefingFor(Script, deal, member.CharacterId);

            Assert.NotNull(briefing);
            Assert.NotEmpty(briefing!.Witnessed);
            Assert.DoesNotContain("{", briefing.Motive);
        }

        // And every clue still reads as something, tampered or not.
        foreach (var clue in trip.Mystery.Clues)
        {
            var text = Compiler.ClueText(Script, clue);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("{", text);
        }
    }

    [Fact]
    public async Task An_evening_survives_a_restart()
    {
        await using var fx = new StoreFixture();

        await fx.Store.MutateAsync(t =>
        {
            MysteryService.DealGame(t, Script, 606);
            MysteryService.Start(t);
            MysteryService.GoToRound(t, Script, 3);
            MysteryService.OpenTrial(t, "trial_1", Start);
        }, TripArea.Mystery);

        await fx.Store.FlushAsync();

        var seed = fx.Store.Current.Mystery.Deal!.Seed;
        var reloaded = await fx.RestartAsync();

        // TESTING.md lists "kill the server mid-round, restart" as a scenario because it expects
        // that to happen. The deal, the round and the open trial all have to come back.
        Assert.Equal(seed, reloaded.Current.Mystery.Deal!.Seed);
        Assert.Equal(3, reloaded.Current.Mystery.CurrentRoundIndex);
        Assert.True(reloaded.Current.Mystery.Active);
        Assert.Single(reloaded.Current.Mystery.Trials);
        Assert.Equal(9, reloaded.Current.Mystery.Clues.Count);

        // And the badge tokens, which are printed on name tags and must not change.
        Assert.All(reloaded.Current.Mystery.Deal!.Cast, c => Assert.Equal(12, c.BadgeToken.Length));
    }
}
