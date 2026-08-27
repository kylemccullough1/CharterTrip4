namespace CharterTrip.Core.Mystery.Script;

/// <summary>
/// The whole authored game, loaded once and never changed.
///
/// This is deliberately not behind an interface. ITripStore is an interface because it wraps file
/// I/O that might one day become a database; this is immutable data, so a test that wants a
/// different script constructs one. Registered as a singleton because loading it twice would be
/// waste, not because anything about it is stateful.
///
/// Nothing here is ever written. What a particular game generated, and what the room has done to
/// it since, live in trip.json instead.
/// </summary>
public sealed record MysteryScript
{
    public IReadOnlyList<ScriptCharacter> Characters { get; init; } = [];
    public ScriptZoneBook Zones { get; init; } = new();
    public ScriptFactionBook Factions { get; init; } = new();
    public ScriptRoundBook Rounds { get; init; } = new();
    public ScriptStoryBeats StoryBeats { get; init; } = new();
    public ScriptPromptBook Prompts { get; init; } = new();
    public ScriptGhostBook Ghosts { get; init; } = new();

    public ScriptCharacter? CharacterById(string id) =>
        Characters.FirstOrDefault(c => c.Id == id);

    /// <summary>The 15 characters the killer draw may consider — everyone with at least one guilt
    /// slot and no pinned faction.</summary>
    public IEnumerable<ScriptCharacter> KillerEligible =>
        Characters.Where(c => !c.IneligibleAsKiller);

    /// <summary>The characters who can fill one named guilt slot: access, means, or signature.</summary>
    public IEnumerable<ScriptCharacter> ForSlot(string slot) =>
        KillerEligible.Where(c => c.Slots.Contains(slot));

    /// <summary>
    /// Everything structurally wrong with this script, as sentences. Empty means it is coherent.
    ///
    /// This runs at startup rather than only in tests: a content file edited by hand the afternoon
    /// of the party should fail loudly then, not halfway through dealing a game.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (Characters.Count != 21)
            problems.Add($"Expected 21 characters, found {Characters.Count}.");

        var duplicateIds = Characters.GroupBy(c => c.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateIds.Count > 0)
            problems.Add($"Duplicate character ids: {string.Join(", ", duplicateIds)}.");

        if (Factions.TotalSeats != Characters.Count)
            problems.Add(
                $"Faction seats total {Factions.TotalSeats} but there are {Characters.Count} characters — " +
                "somebody would have no role.");

        // Every zone a character can stand in, and every faction they can be pinned to, has to exist.
        var zoneIds = Zones.Zones.Select(z => z.Id).ToHashSet();
        var factionIds = Factions.Factions.Select(f => f.Id).ToHashSet();
        var slotIds = new[] { "access", "means", "signature" }.ToHashSet();

        foreach (var character in Characters)
        {
            foreach (var zone in character.Zones.Where(z => !zoneIds.Contains(z)))
                problems.Add($"{character.Id} can be placed in '{zone}', which is not a zone.");

            foreach (var slot in character.Slots.Where(s => !slotIds.Contains(s)))
                problems.Add($"{character.Id} carries slot '{slot}', which is not a guilt slot.");

            if (character.FixedFaction is { } pinned && !factionIds.Contains(pinned))
                problems.Add($"{character.Id} is pinned to faction '{pinned}', which does not exist.");

            if (character.Trace.AnchorZone is { } anchor && !zoneIds.Contains(anchor))
                problems.Add($"{character.Id}'s trace is anchored to '{anchor}', which is not a zone.");

            if (character.Zones.Count == 0)
                problems.Add($"{character.Id} has no zones and could never be placed.");
        }

        // Placement has to be possible at all: 21 people into the playable rooms.
        var playable = Zones.Playable.ToList();
        var minSeats = playable.Sum(z => z.Capacity.Min);
        var maxSeats = playable.Sum(z => z.Capacity.Max);

        if (Characters.Count > maxSeats)
            problems.Add($"{Characters.Count} characters will not fit in {maxSeats} seats.");
        if (Characters.Count < minSeats)
            problems.Add(
                $"{Characters.Count} characters cannot meet a minimum of {minSeats} across " +
                $"{playable.Count} rooms — placement is unsatisfiable.");

        foreach (var zone in playable.Where(z => z.Capacity.Min > z.Capacity.Max))
            problems.Add($"Zone '{zone.Id}' has min capacity above its max.");

        foreach (var zone in Zones.Zones)
        {
            foreach (var neighbour in zone.Adjacent.Where(a => !zoneIds.Contains(a)))
                problems.Add($"Zone '{zone.Id}' is adjacent to '{neighbour}', which is not a zone.");
        }

        // Each guilt slot needs someone eligible to fill it, or the draw dead-ends.
        foreach (var slot in slotIds)
        {
            var supply = ForSlot(slot).Count();
            if (supply == 0) problems.Add($"No eligible character can fill the '{slot}' slot.");
        }

        // The access killer has to be reachable: someone slot-tagged for access must be placeable
        // in a zone that grants it.
        var accessZones = Zones.AccessGranting.Select(z => z.Id).ToHashSet();
        if (!ForSlot("access").Any(c => c.Zones.Any(accessZones.Contains)))
            problems.Add("No access-tagged character can be placed in an access-granting zone.");

        // Three killers in distinct zones is a hard invariant of the deal, so there have to be at
        // least three access-granting rooms' worth of somewhere to put them.
        if (Zones.AccessGranting.Count() < 1)
            problems.Add("No zone grants access to the study.");

        if (Rounds.TotalRuntimeMinutes != Rounds.ScheduledMinutes)
            problems.Add(
                $"rounds.json says {Rounds.TotalRuntimeMinutes} minutes but its rounds sum to " +
                $"{Rounds.ScheduledMinutes}.");

        var trials = Rounds.Trials.ToList();
        if (trials.Count != 3)
            problems.Add($"Expected 3 trials, found {trials.Count}.");

        // Every method beat has to name a real character, or the study scene cannot be composed.
        foreach (var id in StoryBeats.MethodBeats.Keys.Where(k => CharacterById(k) is null))
            problems.Add($"story_beats has a method beat for '{id}', who is not a character.");

        foreach (var route in StoryBeats.AccessBeats.Keys.Where(r => !Zones.AccessRoutes.ContainsKey(r)))
            problems.Add($"story_beats has an access beat for route '{route}', which is not a route.");

        foreach (var (route, detail) in Zones.AccessRoutes)
        {
            foreach (var zone in detail.Zones.Where(z => !zoneIds.Contains(z)))
                problems.Add($"Access route '{route}' names zone '{zone}', which does not exist.");
        }

        return problems;
    }
}
