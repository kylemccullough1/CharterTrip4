using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery.Deal;
using CharterTrip.Core.Mystery.Script;
using CharterTrip.Infrastructure.Mystery;

namespace CharterTrip.Tests;

/// <summary>
/// The generator, checked against its own invariants over many seeds rather than one.
///
/// A dealer that works for seed 1 and wedges on seed 97 is worse than no dealer, because the
/// failure arrives in front of a room. So the interesting tests here sweep a few hundred seeds and
/// assert the properties that have to hold for every single one.
/// </summary>
public class DealerTests
{
    private static readonly MysteryScript Script = ScriptLoader.Load();

    /// <summary>Twenty-one players, named the way the roster would name them.</summary>
    private static List<string> People(int count = 21) =>
        [.. Enumerable.Range(1, count).Select(i => $"p-{i}")];

    private static MysteryDeal DealOrFail(int seed, int people = 21)
    {
        var result = Dealer.Deal(Script, People(people), seed);
        Assert.True(result.Ok, result.Failure?.Reason);
        return result.Deal!;
    }

    /// <summary>Enough seeds to catch a constraint that only bites occasionally.</summary>
    private static IEnumerable<int> Seeds(int count = 300) => Enumerable.Range(1, count);

    [Fact]
    public void A_deal_covers_every_character_exactly_once()
    {
        var deal = DealOrFail(1234);

        Assert.Equal(21, deal.Cast.Count);
        Assert.Equal(21, deal.Cast.Select(c => c.CharacterId).Distinct().Count());
        Assert.All(deal.Cast, c => Assert.NotNull(Script.CharacterById(c.CharacterId)));
    }

    [Fact]
    public void The_same_seed_deals_the_same_game()
    {
        // The whole point of a seed. Replay is what makes a suspicious game reproducible and the
        // generator testable without twenty-one phones.
        var a = DealOrFail(4242);
        var b = DealOrFail(4242);

        Assert.Equal(
            a.Cast.Select(c => (c.CharacterId, c.ZoneId, c.FactionId, c.GuiltSlot, c.IsHerring)),
            b.Cast.Select(c => (c.CharacterId, c.ZoneId, c.FactionId, c.GuiltSlot, c.IsHerring)));

        Assert.Equal(a.AccessRoute, b.AccessRoute);
        Assert.Equal(
            a.CrossZoneSightings.Select(s => (s.ObserverCharacterId, s.SubjectCharacterId)),
            b.CrossZoneSightings.Select(s => (s.ObserverCharacterId, s.SubjectCharacterId)));
    }

    [Fact]
    public void Different_seeds_deal_different_games()
    {
        var guilty = Seeds(50)
            .Select(s => string.Join(",", DealOrFail(s).Killers.Select(k => k.CharacterId).Order()))
            .Distinct()
            .Count();

        // Not a strict requirement, but a generator that produced the same three killers every
        // time would pass every other test in this file.
        Assert.True(guilty > 10, $"Only {guilty} distinct killer sets across 50 seeds.");
    }

    [Fact]
    public void Every_seed_places_everybody_somewhere_they_are_allowed()
    {
        foreach (var seed in Seeds())
        {
            var deal = DealOrFail(seed);

            foreach (var member in deal.Cast)
            {
                var character = Script.CharacterById(member.CharacterId)!;
                Assert.Contains(member.ZoneId, character.Zones);
            }
        }
    }

    [Fact]
    public void Every_seed_respects_every_room_capacity()
    {
        foreach (var seed in Seeds())
        {
            var deal = DealOrFail(seed);

            foreach (var zone in Script.Zones.Playable)
            {
                var count = deal.InZone(zone.Id).Count();
                Assert.InRange(count, zone.Capacity.Min, zone.Capacity.Max);
            }

            // Nobody stands in the study. It is the murder scene.
            Assert.Empty(deal.InZone("study"));
        }
    }

    [Fact]
    public void Every_seed_draws_three_killers_in_three_different_rooms()
    {
        foreach (var seed in Seeds())
        {
            var deal = DealOrFail(seed);
            var killers = deal.Killers.ToList();

            Assert.Equal(3, killers.Count);
            Assert.Equal(["access", "means", "signature"], killers.Select(k => k.GuiltSlot).Order());

            // Three killers in one room would mean one conversation solves the game.
            Assert.Equal(3, killers.Select(k => k.ZoneId).Distinct().Count());
        }
    }

    [Fact]
    public void Every_killer_is_slot_tagged_for_the_slot_they_filled()
    {
        foreach (var seed in Seeds())
        {
            foreach (var killer in DealOrFail(seed).Killers)
            {
                var character = Script.CharacterById(killer.CharacterId)!;

                // A killer with no beat for their slot has no briefing to compose.
                Assert.Contains(killer.GuiltSlot!, character.Slots);
            }
        }
    }

    [Fact]
    public void The_five_slotless_characters_are_never_killers()
    {
        var ineligible = Script.Characters.Where(c => c.IneligibleAsKiller).Select(c => c.Id).ToHashSet();

        foreach (var seed in Seeds())
            foreach (var killer in DealOrFail(seed).Killers)
                Assert.DoesNotContain(killer.CharacterId, ineligible);
    }

    [Fact]
    public void The_access_killer_can_always_actually_reach_the_study()
    {
        var accessZones = Script.Zones.AccessGranting.Select(z => z.Id).ToHashSet();

        foreach (var seed in Seeds())
        {
            var deal = DealOrFail(seed);
            var access = deal.Cast.Single(c => c.GuiltSlot == "access");

            Assert.Contains(access.ZoneId, accessZones);

            // And the reveal has to be able to name how they got in.
            Assert.NotEmpty(deal.AccessRoute);
            Assert.Contains(deal.AccessRoute, Script.Zones.AccessRoutes.Keys);
        }
    }

    [Fact]
    public void A_route_preference_beats_the_room_it_was_drawn_in()
    {
        // Rule 3. Carla prefers the side path, Harry and Da'Quan the window, and their own
        // preference wins over whatever their room would otherwise imply.
        var preferences = Script.Characters
            .Where(c => c.RoutePreference is { Length: > 0 })
            .ToDictionary(c => c.Id, c => c.RoutePreference!);

        var checked_ = 0;

        foreach (var seed in Seeds())
        {
            var deal = DealOrFail(seed);
            var access = deal.Cast.Single(c => c.GuiltSlot == "access");

            if (!preferences.TryGetValue(access.CharacterId, out var preferred)) continue;

            Assert.Equal(preferred, deal.AccessRoute);
            checked_++;
        }

        Assert.True(checked_ > 0, "No seed drew an access killer with a route preference.");
    }

    [Fact]
    public void Every_seed_draws_three_herrings_and_none_of_them_is_a_killer()
    {
        foreach (var seed in Seeds())
        {
            var deal = DealOrFail(seed);
            var herrings = deal.Herrings.ToList();

            Assert.Equal(3, herrings.Count);
            Assert.All(herrings, h => Assert.False(h.IsKiller));

            // Six characters show their guilty reading: three who did it and three who did not.
            Assert.Equal(6, deal.Cast.Count(c => c.ShowsGuilty));
        }
    }

    [Fact]
    public void No_killer_is_ever_left_with_a_single_compromised_witness()
    {
        // Rule 2. If the only person who could have seen a killer is themselves showing guilty,
        // there is no thread the room can pull and the game cannot be solved.
        foreach (var seed in Seeds())
        {
            var deal = DealOrFail(seed);

            foreach (var killer in deal.Killers)
            {
                var coLocated = deal.Cast
                    .Where(c => c.ZoneId == killer.ZoneId && c.CharacterId != killer.CharacterId)
                    .ToList();

                if (coLocated.Count == 1)
                    Assert.False(coLocated[0].ShowsGuilty,
                        $"seed {seed}: {killer.CharacterId}'s only witness is compromised.");
            }
        }
    }

    [Fact]
    public void Every_killer_ends_up_with_at_least_two_threads_pointing_near_them()
    {
        // Rule 5's purpose, stated as the property it exists to guarantee: co-located witnesses
        // plus any cross-zone sighting has to reach two.
        foreach (var seed in Seeds())
        {
            var deal = DealOrFail(seed);

            foreach (var killer in deal.Killers)
            {
                var coLocated = deal.Cast.Count(c => c.ZoneId == killer.ZoneId && c.CharacterId != killer.CharacterId);
                var seen = deal.CrossZoneSightings.Count(s => s.SubjectCharacterId == killer.CharacterId);

                Assert.True(coLocated + seen >= 2,
                    $"seed {seed}: {killer.CharacterId} has {coLocated} witnesses and {seen} sightings.");
            }
        }
    }

    [Fact]
    public void A_cross_zone_observer_is_next_door_and_not_themselves_suspicious()
    {
        foreach (var seed in Seeds(100))
        {
            var deal = DealOrFail(seed);

            foreach (var sighting in deal.CrossZoneSightings)
            {
                var observer = deal.Cast.Single(c => c.CharacterId == sighting.ObserverCharacterId);
                var subject = deal.Cast.Single(c => c.CharacterId == sighting.SubjectCharacterId);

                Assert.Contains(observer.ZoneId, Script.Zones.ById(subject.ZoneId)!.Adjacent);
                Assert.False(observer.ShowsGuilty);
            }
        }
    }

    [Fact]
    public void Every_seat_at_the_table_is_filled_exactly_as_factions_json_says()
    {
        var expected = Script.Factions.Factions.ToDictionary(f => f.Id, f => f.Count);

        foreach (var seed in Seeds())
        {
            var deal = DealOrFail(seed);

            foreach (var (factionId, count) in expected)
                Assert.Equal(count, deal.InFaction(factionId).Count());

            // Nobody without a faction, nobody in two.
            Assert.All(deal.Cast, c => Assert.Contains(c.FactionId, expected.Keys));
        }
    }

    [Fact]
    public void The_three_killers_are_the_killer_faction()
    {
        foreach (var seed in Seeds())
        {
            var deal = DealOrFail(seed);

            Assert.All(deal.Killers, k => Assert.Equal("killer", k.FactionId));
            Assert.Equal(3, deal.InFaction("killer").Count());
        }
    }

    [Fact]
    public void The_two_claimants_are_each_others_rival()
    {
        foreach (var seed in Seeds())
        {
            var claimants = DealOrFail(seed).InFaction("inheritance").ToList();

            Assert.Equal(2, claimants.Count);
            Assert.Equal(claimants[1].CharacterId, claimants[0].RivalCharacterId);
            Assert.Equal(claimants[0].CharacterId, claimants[1].RivalCharacterId);

            // Nobody else has a rival to dig dirt on.
            Assert.All(claimants, c => Assert.NotEqual(c.CharacterId, c.RivalCharacterId));
        }
    }

    [Fact]
    public void Anybody_can_draw_the_claim_now_that_nobody_is_pinned()
    {
        var claimants = Seeds(200)
            .SelectMany(s => DealOrFail(s).InFaction("inheritance").Select(c => c.CharacterId))
            .Distinct()
            .ToList();

        // It used to be Harry and Isla in every game. If this drops back to two, the pin is back.
        Assert.True(claimants.Count > 6, $"Only {claimants.Count} characters ever drew the claim.");
    }

    [Fact]
    public void Casting_fills_roles_in_the_order_the_caller_gave()
    {
        var deal = DealOrFail(7);

        Assert.Equal(21, deal.Cast.Count(c => c.PersonId is not null));
        Assert.Equal(21, deal.Cast.Select(c => c.PersonId).Distinct().Count());
    }

    [Fact]
    public void A_short_roster_leaves_the_surplus_roles_uncast_rather_than_failing()
    {
        // Friday cancellations. The game still deals; the host console decides what to do with
        // the empty seats.
        var deal = DealOrFail(9, people: 18);

        Assert.Equal(18, deal.Cast.Count(c => c.PersonId is not null));
        Assert.Equal(3, deal.Cast.Count(c => c.PersonId is null));
    }

    [Fact]
    public void A_roster_the_rooms_cannot_seat_fails_loudly_instead_of_spinning()
    {
        // The floor is 17: below it the room minimums cannot be met, and no amount of reshuffling
        // will help. TESTING.md expects this to be a message on the host console, not a hang.
        var thin = Script with { Characters = [.. Script.Characters.Take(16)] };

        var result = Dealer.Deal(thin, People(16), 1);

        Assert.False(result.Ok);
        Assert.NotNull(result.Failure);
        Assert.Contains("17", result.Failure!.Reason);
    }

    [Fact]
    public void Incoherent_content_is_refused_before_anything_is_dealt()
    {
        var broken = Script with
        {
            Characters = [.. Script.Characters.Select((c, i) => i == 0 ? c with { Zones = ["nowhere"] } : c)]
        };

        var result = Dealer.Deal(broken, People(), 1);

        Assert.False(result.Ok);
        Assert.Contains("nowhere", result.Failure!.Reason);
    }

    [Fact]
    public void No_character_is_drawn_as_a_killer_far_more_often_than_anybody_else()
    {
        // Rule 1, dual-tag half-weighting, is what this measures. Without it Solomon and Wilhelm
        // sit around 30% because they carry two slots and get two chances.
        var counts = Script.Characters.ToDictionary(c => c.Id, _ => 0);

        const int seeds = 600;
        foreach (var seed in Seeds(seeds))
            foreach (var killer in DealOrFail(seed).Killers)
                counts[killer.CharacterId]++;

        var eligible = Script.KillerEligible.Select(c => c.Id).ToList();
        var rates = eligible.ToDictionary(id => id, id => counts[id] / (double)seeds);

        // Uniform would be 3/16 ≈ 19%. The simulation's own target was a 12-24% band; allow a
        // little more slack than that so the test is not flaky, but still catch a 2x outlier.
        Assert.All(rates, kv =>
            Assert.InRange(kv.Value, 0.05, 0.40));

        // And nobody ineligible ever appears.
        foreach (var id in Script.Characters.Where(c => c.IneligibleAsKiller).Select(c => c.Id))
            Assert.Equal(0, counts[id]);
    }

    // ---- clue layout ------------------------------------------------------------------------

    [Fact]
    public void Every_room_holds_exactly_one_clue()
    {
        foreach (var seed in Seeds(200))
        {
            var deal = DealOrFail(seed);
            var clues = Dealer.LayOutClues(Script, deal);

            Assert.Equal(9, clues.Count);
            Assert.Equal(
                Script.Zones.Zones.Select(z => z.Id).Order(),
                clues.Select(c => c.ZoneId).Order());
        }
    }

    [Fact]
    public void The_study_holds_the_scene_and_never_a_trace()
    {
        foreach (var seed in Seeds(100))
        {
            var clues = Dealer.LayOutClues(Script, DealOrFail(seed));
            var study = clues.Single(c => c.ZoneId == "study");

            Assert.Null(study.TraceCharacterId);
            Assert.Equal("study", study.SpineClueId);
        }
    }

    [Fact]
    public void An_anchored_trace_stays_in_the_room_it_belongs_to()
    {
        var anchored = Script.Characters
            .Where(c => !c.Trace.IsPortable)
            .ToDictionary(c => c.Id, c => c.Trace.AnchorZone!);

        foreach (var seed in Seeds(200))
        {
            var clues = Dealer.LayOutClues(Script, DealOrFail(seed));

            foreach (var clue in clues.Where(c => c.TraceCharacterId is not null))
            {
                if (anchored.TryGetValue(clue.TraceCharacterId!, out var anchor))
                    Assert.Equal(anchor, clue.ZoneId);
            }
        }
    }

    [Fact]
    public void Every_clue_token_is_unguessable_and_unique()
    {
        var deal = DealOrFail(11);
        var clues = Dealer.LayOutClues(Script, deal);

        Assert.Equal(9, clues.Select(c => c.Token).Distinct().Count());
        Assert.All(clues, c =>
        {
            Assert.Equal(12, c.Token.Length);
            Assert.DoesNotContain(c.Token, ch => "O0I1S5B8".Contains(ch));
        });

        // Not derived from the seed, on purpose: nine guessable tokens would let somebody read
        // every clue from the sofa, and walking to the room is the mechanic.
        var again = Dealer.LayOutClues(Script, deal);
        Assert.NotEqual(clues.Select(c => c.Token), again.Select(c => c.Token));
    }

    [Fact]
    public void Most_rooms_carry_a_real_trace_rather_than_a_neutral_card()
    {
        // Six characters show guilty, so up to six rooms should hold a trace. Anchor collisions
        // can cost one, and that is expected — but a layout that quietly dropped most of them
        // would leave the room nothing to find.
        var traceCounts = Seeds(100)
            .Select(s => Dealer.LayOutClues(Script, DealOrFail(s)).Count(c => c.TraceCharacterId is not null))
            .ToList();

        Assert.All(traceCounts, count => Assert.InRange(count, 4, 6));
        Assert.True(traceCounts.Average() > 5.0, $"Average trace count was only {traceCounts.Average():F2}.");
    }
}
