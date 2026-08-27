using System.Security.Cryptography;
using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery.Script;

namespace CharterTrip.Core.Mystery.Deal;

/// <summary>Why a deal could not be produced. Reported to the host console rather than thrown.</summary>
public sealed record DealFailure(string Reason);

/// <summary>Either a game or an explanation. Never both, never neither.</summary>
public sealed record DealResult(MysteryDeal? Deal, DealFailure? Failure)
{
    public bool Ok => Deal is not null;

    public static DealResult Success(MysteryDeal deal) => new(deal, null);
    public static DealResult Failed(string reason) => new(null, new DealFailure(reason));
}

/// <summary>
/// Turns the script and a seed into one specific evening.
///
/// A pure function of <c>(script, personIds, seed)</c>, which is the property everything else rests
/// on: <c>?seed=1234</c> replays the same game, so the generator can be tested and a suspicious
/// game can be reproduced without twenty-one phones. Nothing here reads the clock or global state
/// — except the clue tokens, which are deliberately unguessable and therefore deliberately not
/// reproducible. They are labels, not game logic.
///
/// The algorithm is the nine steps in the data set's own README plus its five post-simulation
/// rules. Steps 7 and 9 — composing prose and printing — are not here; this decides, the Compiler
/// says.
/// </summary>
public static class Dealer
{
    /// <summary>
    /// How many times to retry placement before giving up.
    ///
    /// Bounded on purpose. A roster the zone capacities cannot satisfy is not a slow deal, it is an
    /// impossible one, and a generator that spins forever on it looks identical to a hung app in
    /// front of a room. Fail loudly to the host console instead.
    /// </summary>
    private const int MaxPlacementAttempts = 200;

    private const int MaxDrawAttempts = 200;

    /// <summary>Herring weighting: slot-tagged characters look like killers, so favour them.</summary>
    private const double SlotTaggedHerringWeight = 3.0;

    /// <summary>Flavour weights named in <c>factions.json</c>'s own assignment notes.</summary>
    private static readonly string[] DetectiveFlavour = ["remington", "martha", "molly"];
    private static readonly string[] JesterFlavour = ["hugo", "emilia", "santiago", "daquan"];
    private const double FlavourWeight = 3.0;

    /// <summary>
    /// Deal a game.
    ///
    /// <paramref name="personIds"/> is who is actually playing, in the order they should be cast.
    /// Fewer people than characters leaves the surplus roles uncast, which the host console fills
    /// or leaves alone; more people than roles is the caller's problem to have trimmed already.
    /// </summary>
    public static DealResult Deal(MysteryScript script, IReadOnlyList<string> personIds, int seed)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(personIds);

        var problems = script.Validate();
        if (problems.Count > 0)
            return DealResult.Failed("The content is not coherent: " + string.Join(" ", problems));

        var playable = script.Zones.Playable.ToList();
        var minSeats = playable.Sum(z => z.Capacity.Min);

        if (script.Characters.Count < minSeats)
            return DealResult.Failed(
                $"{script.Characters.Count} characters cannot fill {playable.Count} rooms that want " +
                $"{minSeats} between them. Reduce the room minimums or add people.");

        var rng = new Random(seed);

        for (var attempt = 0; attempt < MaxDrawAttempts; attempt++)
        {
            var placement = Place(script, rng);
            if (placement is null) continue;

            var cast = BuildCast(script, placement);

            if (!DrawKillers(script, cast, rng)) continue;
            if (!DrawHerrings(script, cast, rng)) continue;

            // Rule 2: a killer whose single co-located witness is themselves compromised has no
            // usable thread pointing at them, and the room cannot solve what it cannot hear about.
            if (HasLoneCompromisedWitness(cast)) continue;

            DrawFactions(script, cast, rng);
            PairInheritanceRivals(cast);
            Cast(cast, personIds);

            var deal = new MysteryDeal
            {
                Seed = seed,
                Cast = cast,
                AccessRoute = AccessRouteFor(script, cast),
                CrossZoneSightings = CrossZoneSightings(script, cast)
            };

            return DealResult.Success(deal);
        }

        return DealResult.Failed(
            $"Could not satisfy the generator's constraints in {MaxDrawAttempts} attempts. " +
            "This usually means the roster and the zone capacities disagree.");
    }

    /// <summary>
    /// The nine clue cards, one per zone. Separate from the deal because it depends on it and is
    /// consumed by a different thing — the print sheet.
    /// </summary>
    public static IReadOnlyList<MysteryClue> LayOutClues(MysteryScript script, MysteryDeal deal)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(deal);

        var clues = new List<MysteryClue>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        // The study is the scene. It takes no players, so it never holds a trace.
        foreach (var zone in script.Zones.Zones.Where(z => !z.PlayersAllowed))
        {
            clues.Add(SpineClue(zone.Id));
            taken.Add(zone.Id);
        }

        // Rule 4: anchored traces go first and never move — the clue physically belongs in that
        // room. Portable ones then fill around them.
        var guilty = deal.Cast
            .Where(c => c.ShowsGuilty)
            .Select(c => (Member: c, Character: script.CharacterById(c.CharacterId)))
            .Where(x => x.Character is not null)
            .ToList();

        foreach (var (member, character) in guilty.Where(x => !x.Character!.Trace.IsPortable))
        {
            var zoneId = character!.Trace.AnchorZone!;

            // Two anchored traces wanting the same room is the one case the spillover rule cannot
            // resolve, since neither may move. Drop the second rather than overfill the room: the
            // character is still guilty and still has witnesses, they just leave no card behind.
            if (!taken.Add(zoneId)) continue;

            clues.Add(TraceClue(member.CharacterId, zoneId));
        }

        foreach (var (member, character) in guilty.Where(x => x.Character!.Trace.IsPortable))
        {
            var zoneId = ZoneForPortableTrace(script, member.ZoneId, taken);
            if (zoneId is null) continue;

            taken.Add(zoneId);
            clues.Add(TraceClue(member.CharacterId, zoneId));
        }

        // Every remaining room gets a neutral card, so that walking into a room always finds
        // something and an empty room is never a tell.
        foreach (var zone in script.Zones.Playable.Where(z => !taken.Contains(z.Id)))
            clues.Add(SpineClue(zone.Id));

        return [.. clues.OrderBy(c => ZoneOrder(script, c.ZoneId))];
    }

    // ---- placement -------------------------------------------------------------------------

    /// <summary>
    /// Step 1 and 2: everybody into a room they are allowed to be in, every room within capacity.
    ///
    /// Greedy with retries rather than a solver. The constraints are loose — 21 people into rooms
    /// that hold 17 to 29 — so a shuffle plus "prefer rooms still under their minimum" lands almost
    /// every time, and the retry covers the rest. A solver would be more code for the same answer.
    /// </summary>
    private static Dictionary<string, string>? Place(MysteryScript script, Random rng)
    {
        var zones = script.Zones.Playable.ToList();

        for (var attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            var counts = zones.ToDictionary(z => z.Id, _ => 0, StringComparer.Ordinal);
            var placement = new Dictionary<string, string>(StringComparer.Ordinal);

            // Most constrained first, ties broken randomly: a character with two allowed rooms
            // placed last may find both full.
            var order = script.Characters
                .OrderBy(c => c.Zones.Count)
                .ThenBy(_ => rng.Next())
                .ToList();

            var failed = false;

            foreach (var character in order)
            {
                var allowed = character.Zones
                    .Where(z => counts.ContainsKey(z) && counts[z] < Capacity(zones, z).Max)
                    .ToList();

                if (allowed.Count == 0) { failed = true; break; }

                // Rooms still short of their minimum win, so the minimums are met by construction
                // rather than by luck.
                var hungry = allowed.Where(z => counts[z] < Capacity(zones, z).Min).ToList();
                var pool = hungry.Count > 0 ? hungry : allowed;

                var chosen = pool[rng.Next(pool.Count)];
                placement[character.Id] = chosen;
                counts[chosen]++;
            }

            if (failed) continue;
            if (zones.Any(z => counts[z.Id] < z.Capacity.Min)) continue;

            // Step 2's other half: at least two access-tagged characters have to have landed
            // somewhere that reaches the study, or the access killer cannot be drawn.
            var accessZones = script.Zones.AccessGranting.Select(z => z.Id).ToHashSet(StringComparer.Ordinal);
            var reachable = script.ForSlot("access")
                .Count(c => placement.TryGetValue(c.Id, out var z) && accessZones.Contains(z));

            if (reachable < 2) continue;

            return placement;
        }

        return null;
    }

    private static ScriptCapacity Capacity(List<ScriptZone> zones, string zoneId) =>
        zones.First(z => z.Id == zoneId).Capacity;

    private static List<MysteryCastMember> BuildCast(MysteryScript script, Dictionary<string, string> placement) =>
        [.. script.Characters.Select(c => new MysteryCastMember
        {
            CharacterId = c.Id,
            ZoneId = placement[c.Id],
            BadgeToken = NewToken()
        })];

    // ---- the draws -------------------------------------------------------------------------

    /// <summary>
    /// Step 3: one killer per guilt slot, no two in the same room.
    ///
    /// Rule 1 — dual-tag half-weighting — is the reason for the weight: a character tagged for two
    /// slots gets two chances to be drawn, and without halving them Solomon and Wilhelm end up
    /// killers about twice as often as anybody else.
    /// </summary>
    private static bool DrawKillers(
        MysteryScript script, List<MysteryCastMember> cast, Random rng)
    {
        var accessZones = script.Zones.AccessGranting.Select(z => z.Id).ToHashSet(StringComparer.Ordinal);
        var byId = cast.ToDictionary(c => c.CharacterId, StringComparer.Ordinal);

        for (var attempt = 0; attempt < MaxDrawAttempts; attempt++)
        {
            foreach (var member in cast) member.GuiltSlot = null;

            var chosen = new List<MysteryCastMember>();
            var ok = true;

            foreach (var slot in new[] { "access", "means", "signature" })
            {
                var candidates = script.ForSlot(slot)
                    .Select(c => byId[c.Id])
                    .Where(m => m.GuiltSlot is null)
                    .Where(m => chosen.All(k => k.ZoneId != m.ZoneId))
                    .Where(m => slot != "access" || accessZones.Contains(m.ZoneId))
                    .ToList();

                if (candidates.Count == 0) { ok = false; break; }

                var pick = WeightedPick(candidates, m => 1.0 / script.CharacterById(m.CharacterId)!.Slots.Count, rng);
                pick.GuiltSlot = slot;
                chosen.Add(pick);
            }

            if (ok) return true;
        }

        return false;
    }

    /// <summary>
    /// Step 4: three innocents show their guilty reading.
    ///
    /// Weighted toward slot-tagged characters so that a red herring looks exactly as plausible as a
    /// killer — which it does, because it is literally the same authored text.
    /// </summary>
    private static bool DrawHerrings(MysteryScript script, List<MysteryCastMember> cast, Random rng)
    {
        foreach (var member in cast) member.IsHerring = false;

        var pool = cast.Where(m => !m.IsKiller).ToList();
        if (pool.Count < 3) return false;

        for (var i = 0; i < 3; i++)
        {
            var candidates = pool.Where(m => !m.IsHerring).ToList();
            if (candidates.Count == 0) return false;

            var pick = WeightedPick(
                candidates,
                m => script.CharacterById(m.CharacterId)!.Slots.Count > 0 ? SlotTaggedHerringWeight : 1.0,
                rng);

            pick.IsHerring = true;
        }

        return true;
    }

    /// <summary>
    /// Rule 2: reject a draw where a killer's only co-located witness is themselves showing guilty
    /// or is a minion.
    ///
    /// Such a witness is not a thread the room can pull. Either their own guilty reading drowns out
    /// what they saw, or they are on the killers' side and will not say it. Cheap to reject — the
    /// simulation put it at about a third of an extra reshuffle per seed.
    /// </summary>
    private static bool HasLoneCompromisedWitness(List<MysteryCastMember> cast)
    {
        foreach (var killer in cast.Where(m => m.IsKiller))
        {
            var coLocated = cast.Where(m => m.ZoneId == killer.ZoneId && m.CharacterId != killer.CharacterId).ToList();

            if (coLocated.Count == 1 && coLocated[0].ShowsGuilty) return true;
        }

        return false;
    }

    /// <summary>
    /// Step 5: the other five factions share the eighteen non-killers.
    ///
    /// Order matters only in that each draw removes its picks from the pool; the flavour weights
    /// come from <c>factions.json</c>'s own assignment notes and are preferences, never guarantees.
    /// </summary>
    private static void DrawFactions(MysteryScript script, List<MysteryCastMember> cast, Random rng)
    {
        foreach (var member in cast)
            member.FactionId = member.IsKiller ? "killer" : "";

        var pool = cast.Where(m => !m.IsKiller).ToList();

        Take("minion", 2, m => script.CharacterById(m.CharacterId)!.Slots.Contains("signature") ? FlavourWeight : 1.0);
        Take("detective", 3, m => DetectiveFlavour.Contains(m.CharacterId) ? FlavourWeight : 1.0);
        Take("jester", 2, m => JesterFlavour.Contains(m.CharacterId) ? FlavourWeight : 1.0);
        Take("inheritance", 2, _ => 1.0);

        // Everybody left is a guest of the house.
        foreach (var member in pool.Where(m => m.FactionId.Length == 0))
            member.FactionId = "villager";

        void Take(string factionId, int count, Func<MysteryCastMember, double> weight)
        {
            for (var i = 0; i < count; i++)
            {
                var candidates = pool.Where(m => m.FactionId.Length == 0).ToList();
                if (candidates.Count == 0) return;

                WeightedPick(candidates, weight, rng).FactionId = factionId;
            }
        }
    }

    /// <summary>The two claimants are each other's rival, recorded so nothing can disagree later.</summary>
    private static void PairInheritanceRivals(List<MysteryCastMember> cast)
    {
        var claimants = cast.Where(m => m.FactionId == "inheritance").ToList();
        if (claimants.Count != 2) return;

        claimants[0].RivalCharacterId = claimants[1].CharacterId;
        claimants[1].RivalCharacterId = claimants[0].CharacterId;
    }

    /// <summary>Rule 3: the access killer's own route preference beats whatever their room implies.</summary>
    private static string AccessRouteFor(MysteryScript script, List<MysteryCastMember> cast)
    {
        var killer = cast.FirstOrDefault(m => m.GuiltSlot == "access");
        if (killer is null) return "";

        var character = script.CharacterById(killer.CharacterId);
        if (character?.RoutePreference is { Length: > 0 } preferred) return preferred;

        return script.Zones.AccessRoutes
            .FirstOrDefault(r => r.Value.Zones.Contains(killer.ZoneId)).Key ?? "";
    }

    /// <summary>
    /// Rule 5: a killer alone with one witness gets seen from the next room too.
    ///
    /// Without it, a killer can end the evening with a single thread pointing at them, and if that
    /// one person is quiet the game has no way to be solved. Recommended rather than required in
    /// the spec; taken, because the failure it prevents is the game not working.
    /// </summary>
    private static List<MysterySighting> CrossZoneSightings(MysteryScript script, List<MysteryCastMember> cast)
    {
        var sightings = new List<MysterySighting>();

        foreach (var killer in cast.Where(m => m.IsKiller))
        {
            var coLocated = cast.Count(m => m.ZoneId == killer.ZoneId && m.CharacterId != killer.CharacterId);
            if (coLocated != 1) continue;

            var adjacent = script.Zones.ById(killer.ZoneId)?.Adjacent ?? [];

            // Deterministic: the first eligible neighbour in zone order, then the first eligible
            // person in that room. No rng, so this cannot shift a replay.
            var observer = cast
                .Where(m => adjacent.Contains(m.ZoneId) && !m.ShowsGuilty)
                .OrderBy(m => m.ZoneId, StringComparer.Ordinal)
                .ThenBy(m => m.CharacterId, StringComparer.Ordinal)
                .FirstOrDefault();

            if (observer is null) continue;

            sightings.Add(new MysterySighting
            {
                ObserverCharacterId = observer.CharacterId,
                SubjectCharacterId = killer.CharacterId
            });
        }

        return sightings;
    }

    /// <summary>
    /// Put people in the roles, in the order given.
    ///
    /// Straight assignment. Gender is not an input, and the caller decides the order — shuffled for
    /// a random cast, or arranged for a hand-picked one.
    /// </summary>
    private static void Cast(List<MysteryCastMember> cast, IReadOnlyList<string> personIds)
    {
        for (var i = 0; i < cast.Count && i < personIds.Count; i++)
            cast[i].PersonId = personIds[i];
    }

    // ---- clue placement helpers -----------------------------------------------------------

    private static string? ZoneForPortableTrace(MysteryScript script, string preferred, HashSet<string> taken)
    {
        if (!taken.Contains(preferred)) return preferred;

        // Spill to an adjacent room that has nothing yet, in a fixed order so a replay matches.
        var adjacent = script.Zones.ById(preferred)?.Adjacent ?? [];

        var spill = adjacent
            .Where(z => !taken.Contains(z))
            .Where(z => script.Zones.ById(z) is { PlayersAllowed: true })
            .OrderBy(z => z, StringComparer.Ordinal)
            .FirstOrDefault();

        if (spill is not null) return spill;

        // Last resort: anywhere still empty, so a guilty character is not silently left without a
        // card just because their corner of the house filled up.
        return script.Zones.Playable
            .Where(z => !taken.Contains(z.Id))
            .OrderBy(z => z.Id, StringComparer.Ordinal)
            .FirstOrDefault()?.Id;
    }

    private static MysteryClue TraceClue(string characterId, string zoneId) => new()
    {
        Id = $"mc-{zoneId}",
        Token = NewClueToken(),
        ZoneId = zoneId,
        TraceCharacterId = characterId
    };

    /// <summary>
    /// A neutral card, so that every room rewards walking into it and an empty room is never a tell.
    ///
    /// The zone id is carried as the spine clue id because the content has no authored text for
    /// neutral clues — the Compiler renders the zone's own <c>clue_spot</c> line instead of
    /// inventing one.
    /// </summary>
    private static MysteryClue SpineClue(string zoneId) => new()
    {
        Id = $"mc-{zoneId}",
        Token = NewClueToken(),
        ZoneId = zoneId,
        SpineClueId = zoneId
    };

    /// <summary>
    /// Deliberately not derived from the seed.
    ///
    /// Nine guessable tokens would let somebody read all nine clues from the sofa, and walking to
    /// the room is the entire mechanic. A token is a label rather than a decision, so it costs the
    /// deal nothing to be unpredictable — a replayed seed produces the same game with different
    /// tokens, which is what you want anyway when reprinting cards.
    /// </summary>
    private static string NewClueToken() => NewToken();

    /// <summary>
    /// A twelve-character token for a printed QR — a clue card or a name tag.
    ///
    /// Same unambiguous alphabet as everything else that gets read off paper.
    /// </summary>
    private static string NewToken()
    {
        const string alphabet = "ACDEFGHJKMNPQRTUVWXY34679";

        return new string(Enumerable.Range(0, 12)
            .Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)])
            .ToArray());
    }

    private static int ZoneOrder(MysteryScript script, string zoneId)
    {
        for (var i = 0; i < script.Zones.Zones.Count; i++)
            if (script.Zones.Zones[i].Id == zoneId) return i;

        return int.MaxValue;
    }

    // ---- weighted choice -------------------------------------------------------------------

    /// <summary>
    /// Pick one, with weights. Draws a single double so the sequence of rng calls is fixed per
    /// pick — which is what keeps a replay identical.
    /// </summary>
    private static T WeightedPick<T>(IReadOnlyList<T> candidates, Func<T, double> weight, Random rng)
    {
        var weights = candidates.Select(weight).Select(w => w > 0 ? w : 0).ToList();
        var total = weights.Sum();

        if (total <= 0) return candidates[rng.Next(candidates.Count)];

        var roll = rng.NextDouble() * total;
        var running = 0.0;

        for (var i = 0; i < candidates.Count; i++)
        {
            running += weights[i];
            if (roll < running) return candidates[i];
        }

        return candidates[^1];
    }
}
