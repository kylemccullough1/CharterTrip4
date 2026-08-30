using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;
using CharterTrip.Infrastructure.Mystery;

namespace CharterTrip.Tests;

/// <summary>
/// The whole evening, through a real store, end to end.
///
/// Every other test checks one rule. This one checks that they compose, because the failure that
/// matters is not a broken rule — it is a game that wedges somewhere in the middle with twenty-one
/// people standing in a room watching nothing happen.
///
/// It walks lobby to reveal exactly the way the night does: twenty-five people come through two
/// doors, every phase is entered in order, all three trials run with a full electorate, and the
/// abilities fire as they unlock.
/// </summary>
public class FullEveningTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 19, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(int minutes) => T0.AddMinutes(minutes);

    // ------------------------------------------------------------------------------------------
    //  The evening
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Sets a game up in a real store with everybody seated, and hands back the store.
    /// </summary>
    private static async Task<StoreFixture> ArrivedAsync()
    {
        var fx = new StoreFixture();

        await fx.Store.MutateAsync(t =>
        {
            StoryLoader.SeedInto(t);
            CastingService.OpenDoors(t, new Random(1));
            PhaseService.GoToPhase(t, MysteryPhase.Assembling, At(0));
        }, TripArea.Mystery);

        // The four organizers take a house part each, then everybody else taps their own name.
        var parts = CastingService.UnclaimedStaffParts(fx.Store.Current).Select(c => c.Id).ToList();
        var organizers = CastingService.Organizers(fx.Store.Current).Select(p => p.Id).ToList();

        foreach (var (person, part) in organizers.Zip(parts))
            await fx.Store.MutateAsync(t => CastingService.ClaimStaffPart(t, person, part), TripArea.Mystery);

        // One at a time, which is also how it happens: twenty-one people at a door, in a queue.
        while (CastingService.Unclaimed(fx.Store.Current).FirstOrDefault() is { } waiting)
            await fx.Store.MutateAsync(
                t => CastingService.ClaimCharacter(t, waiting.Id, new Random(7)), TripArea.Mystery);

        return fx;
    }

    /// <summary>Everybody living votes for somebody, so a trial always resolves.</summary>
    private static async Task EverybodyVotesAsync(StoreFixture fx, int minute)
    {
        var living = TrialService.Living(fx.Store.Current).Select(c => c.Id).ToList();

        await fx.Store.MutateAsync(t =>
        {
            var trial = TrialService.Current(t)!;

            var candidates = trial.Stage == MysteryTrialStage.FinalVote
                ? trial.NomineeCharacterIds
                : living;

            var electorate = trial.Stage == MysteryTrialStage.FinalVote
                ? living.Where(id => !trial.NomineeCharacterIds.Contains(id)).ToList()
                : living;

            // Votes cluster, the way a real room's do — a handful of names collect most of them.
            // Spreading them perfectly evenly is the pathological case and it gets its own test.
            var front = Math.Max(1, Math.Min(candidates.Count, 5));

            for (var i = 0; i < electorate.Count; i++)
            {
                var target = candidates[i % front];
                if (target == electorate[i]) target = candidates[(i + 1) % front];

                TrialService.CastVote(t, electorate[i], target, At(minute));
            }
        }, TripArea.Mystery);
    }

    /// <summary>One whole trial: nominate, tally, defend, vote again, verdict.</summary>
    private static async Task RunTrialAsync(StoreFixture fx, int minute)
    {
        await EverybodyVotesAsync(fx, minute);
        Assert.True(TrialService.EveryoneHasVoted(fx.Store.Current), "somebody never voted");

        await fx.Store.MutateAsync(t => TrialService.CloseNominations(t), TripArea.Mystery);
        await fx.Store.MutateAsync(t => TrialService.BeginDefence(t), TripArea.Mystery);

        // Each nominee has their say. The host taps through; there is no clock.
        while (fx.Store.Current is var trip && TrialService.CurrentSpeaker(trip) is not null)
        {
            var moved = await fx.Store.MutateAsync(t => TrialService.NextSpeaker(t), TripArea.Mystery);
            if (!moved) break;
        }

        await fx.Store.MutateAsync(t => TrialService.OpenFinalVote(t), TripArea.Mystery);
        await EverybodyVotesAsync(fx, minute + 1);
        await fx.Store.MutateAsync(t => TrialService.CloseFinalVote(t, At(minute + 2)), TripArea.Mystery);
    }

    /// <summary>Walk the whole night, phase by phase, doing what each one is for.</summary>
    private static async Task<StoreFixture> PlayAsync()
    {
        var fx = await ArrivedAsync();
        var minute = 10;

        foreach (var phase in MysteryPhases.Order.SkipWhile(p => p != MysteryPhase.Welcome))
        {
            await fx.Store.MutateAsync(t => PhaseService.GoToPhase(t, phase, At(minute)), TripArea.Mystery);
            minute += 5;

            switch (phase)
            {
                case MysteryPhase.Presentation:
                    while (await fx.Store.MutateAsync(t => PhaseService.NextSlide(t), TripArea.Mystery)) { }
                    break;

                case MysteryPhase.Introductions:
                    await IntroduceAsync(fx, minute);
                    await MingleAsync(fx, minute);
                    break;

                case MysteryPhase.Investigation:
                    await InvestigateAsync(fx, minute);
                    break;

                case MysteryPhase.Trial1:
                case MysteryPhase.Trial2:
                case MysteryPhase.Trial3:
                    await RunTrialAsync(fx, minute);
                    minute += 5;
                    break;

                case MysteryPhase.Reveal:
                    await fx.Store.MutateAsync(t => OutcomeService.End(t, At(minute)), TripArea.Mystery);
                    break;
            }
        }

        return fx;
    }

    /// <summary>Braun steps the room through everybody standing up, one at a time.</summary>
    private static async Task IntroduceAsync(StoreFixture fx, int minute)
    {
        while (await fx.Store.MutateAsync(t => PhaseService.NextIntro(t, At(minute)), TripArea.Mystery)) { }
    }

    /// <summary>Everybody scans a few people. Not everybody meets everybody — they never do.</summary>
    private static async Task MingleAsync(StoreFixture fx, int minute)
    {
        var guests = fx.Store.Current.Mystery.Story.Guests.Select(c => c.Id).ToList();

        await fx.Store.MutateAsync(t =>
        {
            for (var i = 0; i < guests.Count; i++)
                foreach (var step in new[] { 1, 4, 7 })
                    ScanShareService.RecordMeeting(t, guests[i], guests[(i + step) % guests.Count], At(minute));
        }, TripArea.Mystery);
    }

    /// <summary>Cards get found, a jester works on one, and the detectives spend a charge.</summary>
    private static async Task InvestigateAsync(StoreFixture fx, int minute)
    {
        var story = fx.Store.Current.Mystery.Story;
        var clues = story.Clues.Select(c => c.Id).ToList();
        var guests = story.Guests.Select(c => c.Id).ToList();

        await fx.Store.MutateAsync(t =>
        {
            for (var i = 0; i < guests.Count; i++)
                ScanShareService.RecordClueScan(t, guests[i], clues[i % clues.Count], At(minute));

            foreach (var jester in story.Guests.Where(c => c.FactionId == "jester"))
                if (AbilityService.TryFire(t, jester.Id, "self_frame", At(minute + 1), mode: "subtle",
                        targetClueId: clues[0]) is not null)
                    ScanShareService.Tamper(t, clues[0], "subtle", jester.Id, jester.Id, At(minute + 1));

            foreach (var detective in story.Guests.Where(c => c.FactionId == "detective"))
                AbilityService.TryFire(t, detective.Id, "sync", At(minute + 2),
                    targetCharacterId: guests[0], result: "testimony");
        }, TripArea.Mystery);
    }

    // ------------------------------------------------------------------------------------------
    //  What has to be true at the end
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_evening_runs_from_the_lobby_to_the_reveal_without_wedging()
    {
        await using var fx = await PlayAsync();
        var mystery = fx.Store.Current.Mystery;

        Assert.Equal(MysteryPhase.Reveal, mystery.Phase);
        Assert.Equal(3, mystery.Play.Trials.Count);
        Assert.All(mystery.Play.Trials, t => Assert.Equal(MysteryTrialStage.Verdict, t.Stage));
        Assert.All(mystery.Play.Trials, t => Assert.NotEmpty(t.ConvictedCharacterIds));
        Assert.NotNull(mystery.Play.Outcome);
    }

    [Fact]
    public async Task Everybody_gets_a_part_and_nobody_gets_two()
    {
        await using var fx = await ArrivedAsync();
        var play = fx.Store.Current.Mystery.Play;

        Assert.Equal(25, play.Cast.Count);
        Assert.All(play.Cast, c => Assert.NotNull(c.PersonId));

        var people = play.Cast.Select(c => c.PersonId!).ToList();
        Assert.Equal(people.Count, people.Distinct(StringComparer.Ordinal).Count());

        Assert.Empty(CastingService.Unclaimed(fx.Store.Current));
        Assert.Empty(CastingService.UnclaimedStaffParts(fx.Store.Current));
        Assert.True(CastingService.CanStart(fx.Store.Current).Ready);
    }

    /// <summary>
    /// Six is the floor, not the count. Ties widen a verdict rather than being broken, so more than
    /// two can go down at once — and nothing downstream may assume otherwise.
    /// </summary>
    [Fact]
    public async Task At_least_six_people_go_to_jail_across_three_trials()
    {
        await using var fx = await PlayAsync();
        var convicted = fx.Store.Current.Mystery.Play.ConvictedCharacterIds.ToList();

        Assert.True(convicted.Count >= 6, $"only {convicted.Count} convicted");
        Assert.Equal(convicted.Count, convicted.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The room splits perfectly evenly and everybody ends up standing.
    ///
    /// This is what ties widening rather than breaking costs: with one vote each, the cut at fourth
    /// place lets all twenty-one through, and the usual rule — the people standing do not judge
    /// themselves — leaves nobody to vote at all. That is a trial that never ends, in front of a
    /// room. It is why the electorate falls back to the nominees, and why a verdict falls back to
    /// the nomination tally.
    /// </summary>
    [Fact]
    public async Task A_room_that_splits_evenly_still_sends_somebody_to_jail()
    {
        await using var fx = await ArrivedAsync();

        await fx.Store.MutateAsync(t => PhaseService.GoToPhase(t, MysteryPhase.Trial1, At(60)), TripArea.Mystery);

        var living = TrialService.Living(fx.Store.Current).Select(c => c.Id).ToList();

        // One vote each, in a ring. Nobody is ahead of anybody.
        await fx.Store.MutateAsync(t =>
        {
            for (var i = 0; i < living.Count; i++)
                TrialService.CastVote(t, living[i], living[(i + 1) % living.Count], At(61));
        }, TripArea.Mystery);

        await fx.Store.MutateAsync(t => TrialService.CloseNominations(t), TripArea.Mystery);
        Assert.Equal(living.Count, TrialService.Current(fx.Store.Current)!.NomineeCharacterIds.Count);

        // Everybody is standing, so everybody votes — and nobody may simply vote for themselves.
        Assert.Equal(living.Count, TrialService.Electorate(fx.Store.Current).Count);

        await fx.Store.MutateAsync(t => TrialService.BeginDefence(t), TripArea.Mystery);
        await fx.Store.MutateAsync(t => TrialService.OpenFinalVote(t), TripArea.Mystery);

        await fx.Store.MutateAsync(t =>
        {
            for (var i = 0; i < living.Count; i++)
                Assert.False(TrialService.CastVote(t, living[i], living[i], At(62)), "voted for themselves");

            // The room converges on two names.
            for (var i = 0; i < living.Count; i++)
                TrialService.CastVote(t, living[i], living[i % 2 == 0 ? 0 : 1], At(62));
        }, TripArea.Mystery);

        await fx.Store.MutateAsync(t => TrialService.CloseFinalVote(t, At(63)), TripArea.Mystery);

        var trial = TrialService.Current(fx.Store.Current)!;
        Assert.Equal(MysteryTrialStage.Verdict, trial.Stage);
        Assert.NotEmpty(trial.ConvictedCharacterIds);
    }

    [Fact]
    public async Task Nobody_convicted_can_vote_in_a_later_trial()
    {
        await using var fx = await PlayAsync();
        var trip = fx.Store.Current;

        foreach (var trial in trip.Mystery.Play.Trials.Skip(1))
        {
            var earlier = trip.Mystery.Play.Trials
                .TakeWhile(t => t != trial)
                .SelectMany(t => t.ConvictedCharacterIds)
                .ToHashSet(StringComparer.Ordinal);

            Assert.All(trial.Nominations, v => Assert.DoesNotContain(v.VoterCharacterId, earlier));
            Assert.All(trial.FinalVotes, v => Assert.DoesNotContain(v.VoterCharacterId, earlier));
        }
    }

    /// <summary>Staff are in the room, not in the game. A ballot for one is a wasted slot.</summary>
    [Fact]
    public async Task The_four_running_the_evening_never_appear_on_a_ballot()
    {
        await using var fx = await PlayAsync();
        var trip = fx.Store.Current;
        var staff = trip.Mystery.Story.StaffParts.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var trial in trip.Mystery.Play.Trials)
        {
            Assert.All(trial.Nominations, v => Assert.DoesNotContain(v.VoterCharacterId, staff));
            Assert.All(trial.Nominations, v => Assert.DoesNotContain(v.TargetCharacterId, staff));
            Assert.DoesNotContain(trial.ConvictedCharacterIds, id => staff.Contains(id));
        }
    }

    /// <summary>
    /// One charge across a whole faction means one use across a whole faction, however many people
    /// press the button.
    /// </summary>
    [Fact]
    public async Task A_shared_charge_is_spent_exactly_once()
    {
        await using var fx = await PlayAsync();
        var trip = fx.Store.Current;

        foreach (var faction in trip.Mystery.Story.Factions)
            foreach (var ability in faction.Abilities.Where(a => a.Shared))
            {
                var spent = trip.Mystery.Play.AbilityUses
                    .Count(u => u.AbilityId == ability.Id && u.FactionId == faction.Id);

                Assert.True(spent <= ability.Charges,
                    $"{faction.Id}.{ability.Id} was spent {spent} times for {ability.Charges} charge(s)");
            }
    }

    [Fact]
    public async Task A_card_is_only_ever_tampered_with_once()
    {
        await using var fx = await PlayAsync();

        // Two jesters both went for the same card. One of them got there first, and the second was
        // refused — otherwise a clue becomes a pile of everybody's belongings.
        Assert.All(fx.Store.Current.Mystery.Play.ClueStates,
            state => Assert.True(state.Tamper is null || state.Tamper.At != default));

        Assert.True(ScanShareService.AnyTampering(fx.Store.Current));
    }

    /// <summary>
    /// Getting to a card first means holding the original. That is the entire reason to walk fast,
    /// and it is derived from two timestamps rather than snapshotted, so it cannot drift.
    /// </summary>
    [Fact]
    public async Task Tampering_never_rewrites_what_somebody_already_scanned()
    {
        await using var fx = new StoreFixture();

        await fx.Store.MutateAsync(t =>
        {
            StoryLoader.SeedInto(t);
            CastingService.OpenDoors(t, new Random(2));

            t.Mystery.Story.Beats.TamperSubtle = "Half-hidden beneath it, {insert}.";
            var clue = t.Mystery.Story.Clues[0];
            clue.Text = "A glass, tipped over and not yet dry.";

            var early = t.Mystery.Story.Guests.First().Id;
            var late = t.Mystery.Story.Guests.Last().Id;
            var jester = t.Mystery.Story.Guests.First(c => c.FactionId == "jester");
            jester.TamperInsert = "a torn cinema ticket";

            ScanShareService.RecordClueScan(t, early, clue.Id, At(1));
            ScanShareService.Tamper(t, clue.Id, "subtle", jester.Id, jester.Id, At(2));
            ScanShareService.RecordClueScan(t, late, clue.Id, At(3));

            Assert.DoesNotContain("cinema ticket", ScanShareService.ReadingFor(t, clue.Id, early));
            Assert.Contains("cinema ticket", ScanShareService.ReadingFor(t, clue.Id, late));

            // The board follows the most recent scan, so the lie only becomes public when somebody
            // physically walks back and re-reads the card.
            Assert.Contains("cinema ticket", ScanShareService.PublicReading(t, clue.Id));
            Assert.DoesNotContain("cinema ticket", ScanShareService.OriginalReading(t, clue.Id));
        }, TripArea.Mystery);
    }

    /// <summary>
    /// The one bug in this game with no recovery. Three people reading "you killed him" while
    /// standing in the Grand Hall cannot be taken back.
    /// </summary>
    [Fact]
    public async Task Nobody_learns_what_they_are_before_the_study()
    {
        await using var fx = await ArrivedAsync();

        foreach (var phase in new[]
                 {
                     MysteryPhase.Welcome, MysteryPhase.Presentation,
                     MysteryPhase.Introductions, MysteryPhase.Murder
                 })
        {
            await fx.Store.MutateAsync(t => PhaseService.GoToPhase(t, phase, At(10)), TripArea.Mystery);
            Assert.False(PhaseService.RolesRevealed(fx.Store.Current), $"roles leaked in {phase}");

            // A faction-addressed objective arriving early tells somebody what they are simply by
            // existing, so the inbox is gated on the same predicate.
            foreach (var killer in fx.Store.Current.Mystery.Story.Killers)
                Assert.DoesNotContain(
                    ObjectiveBus.Inbox(fx.Store.Current, killer.Id),
                    o => o.Audience == MysteryAudience.Faction);
        }

        await fx.Store.MutateAsync(t => PhaseService.GoToPhase(t, MysteryPhase.StudyScene, At(30)), TripArea.Mystery);
        Assert.True(PhaseService.RolesRevealed(fx.Store.Current));
    }

    /// <summary>Withholding the trail is the mechanic. Shown early, every alibi becomes checkable.</summary>
    [Fact]
    public async Task The_scan_trail_stays_shut_until_the_detectives_need_it()
    {
        await using var fx = await ArrivedAsync();
        var clue = fx.Store.Current.Mystery.Story.Clues[0].Id;
        var guest = fx.Store.Current.Mystery.Story.Guests.First().Id;

        await fx.Store.MutateAsync(t =>
        {
            PhaseService.GoToPhase(t, MysteryPhase.Investigation, At(40));
            ScanShareService.RecordClueScan(t, guest, clue, At(41));
        }, TripArea.Mystery);

        // Recorded all along — it has to be, or there would be nothing to open.
        Assert.Single(fx.Store.Current.Mystery.Play.ClueScans);
        Assert.Empty(ScanShareService.Trail(fx.Store.Current, clue));

        // Still shut through the accusation round and the first trial: people accuse on what
        // they were told, not on a log.
        await fx.Store.MutateAsync(t => PhaseService.GoToPhase(t, MysteryPhase.Discussion1, At(60)), TripArea.Mystery);
        Assert.Empty(ScanShareService.Trail(fx.Store.Current, clue));

        await fx.Store.MutateAsync(t => PhaseService.GoToPhase(t, MysteryPhase.Discussion2, At(80)), TripArea.Mystery);
        Assert.Single(ScanShareService.Trail(fx.Store.Current, clue));
    }

    [Fact]
    public async Task Every_ability_becomes_available_at_some_point_in_the_night()
    {
        await using var fx = await PlayAsync();

        Assert.Empty(AbilityService.UnreachableUnlocks(fx.Store.Current));

        var trip = fx.Store.Current;
        foreach (var faction in trip.Mystery.Story.Factions)
            foreach (var ability in faction.Abilities)
                Assert.True(AbilityService.IsUnlocked(trip, ability),
                    $"{faction.Id}.{ability.Id} never unlocked");
    }

    /// <summary>
    /// A phase re-entered from the skip strip must not hand everybody the same instruction twice.
    /// The strip is the safety net, so using it has to be free.
    /// </summary>
    [Fact]
    public async Task Walking_back_into_a_phase_does_not_reissue_its_objectives()
    {
        await using var fx = await ArrivedAsync();

        await fx.Store.MutateAsync(t => PhaseService.GoToPhase(t, MysteryPhase.Introductions, At(20)), TripArea.Mystery);
        var after = fx.Store.Current.Mystery.Play.Objectives.Count;
        Assert.True(after > 0, "the introductions issued nothing at all");

        await fx.Store.MutateAsync(t =>
        {
            PhaseService.GoToPhase(t, MysteryPhase.Presentation, At(21));
            PhaseService.GoToPhase(t, MysteryPhase.Introductions, At(22));
        }, TripArea.Mystery);

        Assert.Equal(after, fx.Store.Current.Mystery.Play.Objectives.Count);
    }

    /// <summary>
    /// The evening has to survive the app falling over. Somebody's laptop lid, a deploy, a crash —
    /// and the answer cannot be asking twenty-one people, in character, who they are.
    /// </summary>
    [Fact]
    public async Task An_evening_survives_a_restart()
    {
        await using var fx = await PlayAsync();

        var before = fx.Store.Current.Mystery;
        var phase = before.Phase;
        var cast = before.Play.Cast.Count;
        var badges = before.Play.Cast.Select(c => c.BadgeToken).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var convicted = before.Play.ConvictedCharacterIds.OrderBy(c => c, StringComparer.Ordinal).ToList();
        var party = before.Play.PartyCode;

        await fx.RestartAsync();

        var after = fx.Store.Current.Mystery;
        Assert.Equal(phase, after.Phase);
        Assert.Equal(cast, after.Play.Cast.Count);
        Assert.Equal(badges, after.Play.Cast.Select(c => c.BadgeToken).OrderBy(t => t, StringComparer.Ordinal));
        Assert.Equal(convicted, after.Play.ConvictedCharacterIds.OrderBy(c => c, StringComparer.Ordinal));
        Assert.Equal(party, after.Play.PartyCode);
        Assert.NotNull(after.Play.Outcome);
        Assert.Equal(25, after.Story.Characters.Count);
    }

    /// <summary>
    /// Rehearsing has to be free. The story is hours of writing; the evening is one night.
    /// </summary>
    [Fact]
    public async Task Discarding_a_game_keeps_everything_somebody_wrote()
    {
        await using var fx = await PlayAsync();

        await fx.Store.MutateAsync(t =>
        {
            t.Mystery.Story.Characters[0].Backstory = "Something somebody actually typed.";
            CastingService.Discard(t);
        }, TripArea.Mystery);

        var mystery = fx.Store.Current.Mystery;
        Assert.Equal(MysteryPhase.Lobby, mystery.Phase);
        Assert.Empty(mystery.Play.Cast);
        Assert.Empty(mystery.Play.Trials);
        Assert.Equal("", mystery.Play.PartyCode);

        Assert.Equal(25, mystery.Story.Characters.Count);
        Assert.Equal("Something somebody actually typed.", mystery.Story.Characters[0].Backstory);
    }

    /// <summary>
    /// The host door is the only thing standing between a guest and the guilty list, and the door
    /// is a filter on one property. Worth its own test rather than a comment.
    /// </summary>
    [Fact]
    public async Task A_guest_who_scans_the_host_code_cannot_take_a_house_part()
    {
        await using var fx = new StoreFixture();

        await fx.Store.MutateAsync(t =>
        {
            StoryLoader.SeedInto(t);
            CastingService.OpenDoors(t, new Random(3));

            var guest = t.Roster.First(p => p.Role != TripRole.Admin);
            var part = t.Mystery.Story.StaffParts.First().Id;

            Assert.False(CastingService.ClaimStaffPart(t, guest.Id, part));
            Assert.Null(t.Mystery.Play.ForCharacter(part)!.PersonId);
        }, TripArea.Mystery);
    }

    [Fact]
    public async Task The_two_doors_are_never_the_same_code()
    {
        // A collision would silently hand every guest the organizers' picker, because the resolver
        // checks the host code first.
        for (var seed = 0; seed < 50; seed++)
        {
            await using var fx = new StoreFixture();
            await fx.Store.MutateAsync(t =>
            {
                StoryLoader.SeedInto(t);
                CastingService.OpenDoors(t, new Random(seed));
            }, TripArea.Mystery);

            var play = fx.Store.Current.Mystery.Play;
            Assert.Equal(5, play.PartyCode.Length);
        }
    }
}
