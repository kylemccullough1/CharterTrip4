using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;
using CharterTrip.Infrastructure.Mystery;
using CharterTrip.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace CharterTrip.Tests;

/// <summary>
/// Writes a trip.json per phase into MYSTERY_DUMP, so the real pages can be rendered against each
/// one. Skipped unless that variable is set — this is a tool, not a test.
/// </summary>
public class DumpPhases
{
    [Fact]
    public async Task Dump()
    {
        var root = Environment.GetEnvironmentVariable("MYSTERY_DUMP");
        if (string.IsNullOrWhiteSpace(root)) return;

        foreach (var phase in MysteryPhases.Order)
        {
            var dir = Path.Combine(root, phase.ToString());
            Directory.CreateDirectory(dir);

            var options = new TripStoreOptions { DataRoot = dir, DebounceMilliseconds = 0 };
            var store = new JsonTripStore(
                Microsoft.Extensions.Options.Options.Create(options),
                NullLogger<JsonTripStore>.Instance,
                new FixedClock(DateTimeOffset.UnixEpoch));

            await BuildAsync(store, phase);
            await store.DisposeAsync();
        }
    }

    private static async Task BuildAsync(JsonTripStore store, MysteryPhase target)
    {
        var now = new DateTimeOffset(2026, 8, 29, 20, 0, 0, TimeSpan.Zero);

        await store.MutateAsync(t =>
        {
            StoryLoader.SeedInto(t);
            CastingService.OpenDoors(t, new Random(4));

            // Everybody in, with a photo on nobody so the monogram path gets exercised too.
            var organizers = CastingService.Organizers(t).Select(p => p.Id).ToList();
            var parts = CastingService.UnclaimedStaffParts(t).Select(c => c.Id).ToList();
            foreach (var (person, part) in organizers.Zip(parts))
                CastingService.ClaimStaffPart(t, person, part);

            foreach (var person in CastingService.Unclaimed(t).ToList())
                CastingService.ClaimCharacter(t, person.Id, new Random(9));

            if (target == MysteryPhase.Lobby)
            {
                CastingService.Discard(t);
                return;
            }

            var guests = t.Mystery.Story.Guests.Select(c => c.Id).ToList();

            foreach (var phase in MysteryPhases.Order.SkipWhile(p => p != MysteryPhase.Assembling))
            {
                PhaseService.GoToPhase(t, phase, now);

                if (phase == MysteryPhase.Introductions)
                    for (var i = 0; i < guests.Count; i++)
                        foreach (var step in new[] { 1, 5 })
                            ScanShareService.RecordMeeting(t, guests[i], guests[(i + step) % guests.Count], now);

                if (phase == MysteryPhase.Investigation)
                {
                    var clues = t.Mystery.Story.Clues.Select(c => c.Id).ToList();
                    for (var i = 0; i < guests.Count; i++)
                        ScanShareService.RecordClueScan(t, guests[i], clues[i % clues.Count], now);

                    var jester = t.Mystery.Story.Guests.First(c => c.FactionId == "jester");
                    ScanShareService.Tamper(t, clues[0], "subtle", jester.Id, jester.Id, now);
                }

                if (MysteryPhases.IsTrial(phase))
                {
                    // Stop mid-trial for the trial phases themselves, so every stage gets seen at
                    // least once across the dump; run it out for the phases after them.
                    RunTrial(t, now, stopAt: phase == target ? MysteryTrialStage.Nominating : null);
                }

                if (phase == MysteryPhase.Reveal) OutcomeService.End(t, now);
                if (phase == target) break;
            }
        }, TripArea.Mystery);

        await store.FlushAsync();
    }

    private static void RunTrial(TripData t, DateTimeOffset now, MysteryTrialStage? stopAt)
    {
        void Vote()
        {
            var trial = TrialService.Current(t)!;
            var voters = TrialService.Electorate(t).Select(c => c.Id).ToList();
            var candidates = trial.Stage == MysteryTrialStage.FinalVote
                ? trial.NomineeCharacterIds
                : TrialService.Living(t).Select(c => c.Id).ToList();

            var front = Math.Max(1, Math.Min(candidates.Count, 5));
            for (var i = 0; i < voters.Count; i++)
            {
                var target = candidates[i % front];
                if (target == voters[i]) target = candidates[(i + 1) % front];
                TrialService.CastVote(t, voters[i], target, now);
            }
        }

        Vote();
        if (stopAt == MysteryTrialStage.Nominating) return;

        TrialService.CloseNominations(t);
        TrialService.BeginDefence(t);
        TrialService.OpenFinalVote(t);
        Vote();
        TrialService.CloseFinalVote(t, now);
    }
}
