using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// What the factions can do, and when.
///
/// Charges are counted from the log of what has been spent rather than decremented on the ability,
/// so a charge shared across a whole faction has exactly one place it can be double-spent from —
/// and that place is inside the store's lock. The killers' single collective charge and the
/// minions' single collective charge are precisely the "two people press the button at the same
/// moment" case.
/// </summary>
public static class AbilityService
{
    /// <summary>Everything this character could ever do. Empty for staff and for villagers.</summary>
    public static IReadOnlyList<MysteryAbility> AbilitiesFor(TripData trip, string characterId)
    {
        var character = trip.Mystery.Story.Character(characterId);
        if (character is null || character.IsStaff) return [];

        return trip.Mystery.Story.Faction(character.FactionId)?.Abilities ?? [];
    }

    /// <summary>
    /// Whether the evening has reached this ability's phase.
    ///
    /// Read alongside <see cref="ChargesRemaining"/> rather than folded into it, because "not yet"
    /// and "all used up" are different things to tell somebody holding a phone.
    /// </summary>
    public static bool IsUnlocked(TripData trip, MysteryAbility ability) =>
        MysteryPhases.AtOrAfter(trip.Mystery.Phase, ability.Unlock);

    /// <summary>
    /// How many uses are left — for this person, or for their whole faction if it is shared.
    /// </summary>
    public static int ChargesRemaining(TripData trip, string characterId, MysteryAbility ability)
    {
        var character = trip.Mystery.Story.Character(characterId);
        if (character is null) return 0;

        var spent = trip.Mystery.Play.AbilityUses.Count(u =>
            u.AbilityId == ability.Id &&
            (ability.Shared ? u.FactionId == character.FactionId : u.ByCharacterId == characterId));

        return Math.Max(0, ability.Charges - spent);
    }

    public static bool CanFire(TripData trip, string characterId, MysteryAbility ability) =>
        IsUnlocked(trip, ability)
        && ChargesRemaining(trip, characterId, ability) > 0
        && !TrialService.IsGhost(trip, characterId);

    /// <summary>
    /// Spend a charge.
    ///
    /// Returns the record if it went through and null if it did not, so the caller can say why
    /// rather than leaving somebody tapping a button that quietly does nothing. Called inside a
    /// mutation, the check and the spend share a lock, which is what makes a shared charge safe.
    /// </summary>
    public static MysteryAbilityUse? TryFire(
        TripData trip,
        string characterId,
        string abilityId,
        DateTimeOffset now,
        string? mode = null,
        string? targetCharacterId = null,
        string? targetClueId = null,
        string? result = null)
    {
        var character = trip.Mystery.Story.Character(characterId);
        if (character is null) return null;

        var ability = AbilitiesFor(trip, characterId).FirstOrDefault(a => a.Id == abilityId);
        if (ability is null || !CanFire(trip, characterId, ability)) return null;

        // A two-mode ability with no mode chosen is a half-finished tap, not a use.
        if (ability.HasModes && (mode is null || ability.Modes.All(m => m.Id != mode))) return null;

        // An ability that answers a question needs the question to be about something. A Hard
        // Question about nobody would spend the charge and say nothing.
        if (result is null && Answers(ability.Id))
        {
            result = ResultFor(trip, ability.Id, targetCharacterId, targetClueId);
            if (result is null) return null;
        }

        var use = new MysteryAbilityUse
        {
            AbilityId = ability.Id,
            ByCharacterId = characterId,
            FactionId = character.FactionId,
            Mode = mode,
            TargetCharacterId = targetCharacterId,
            TargetClueId = targetClueId,
            Result = result,
            At = now
        };

        trip.Mystery.Play.AbilityUses.Add(use);
        return use;
    }

    public const string KillerCheckId = "killer_check";
    public const string TamperCheckId = "tamper_check";

    /// <summary>The abilities that come back with an answer the phone shows.</summary>
    private static bool Answers(string abilityId) => abilityId is KillerCheckId or TamperCheckId;

    /// <summary>
    /// The answer to a detective's question, in the words the phone shows.
    ///
    /// The Hard Question answers as the room would be told — <see cref="TrialService.ShowsAsKiller"/>
    /// — which is exactly what makes an associate taking the blame worth anything. Forensics
    /// reads the card's own state. Null when the question was not about anything.
    /// </summary>
    public static string? ResultFor(TripData trip, string abilityId, string? targetCharacterId, string? targetClueId)
    {
        switch (abilityId)
        {
            case KillerCheckId:
                if (targetCharacterId is null || trip.Mystery.Story.Character(targetCharacterId) is null) return null;
                return TrialService.ShowsAsKiller(trip, targetCharacterId) ? "killer" : "clean";

            case TamperCheckId:
                if (targetClueId is null || trip.Mystery.Story.Clue(targetClueId) is null) return null;
                return trip.Mystery.Play.StateFor(targetClueId)?.Tamper switch
                {
                    null => "untouched",
                    { Mode: "scrub" } => "scrubbed",
                    _ => "planted"
                };

            default:
                return null;
        }
    }

    /// <summary>A result, in capitals, the way the card would say it.</summary>
    public static string ResultLabel(string? result) => result switch
    {
        "killer" => "KILLER",
        "clean" => "NOT A KILLER",
        "untouched" => "Untouched",
        "planted" => "Planted",
        "scrubbed" => "Scrubbed",
        null => "",
        var other => other
    };

    /// <summary>
    /// Abilities that unlock in a phase this evening will never reach.
    ///
    /// Surfaced on the console rather than thrown, because it is a content mistake rather than a
    /// crash — and it is the failure that hides best. A player simply waits all night for a button
    /// that was never going to arrive, and nobody finds out until the reveal.
    /// </summary>
    public static IReadOnlyList<string> UnreachableUnlocks(TripData trip)
    {
        var reachable = MysteryPhases.Order.ToHashSet();

        return trip.Mystery.Story.Factions
            .SelectMany(f => f.Abilities.Select(a => (Faction: f, Ability: a)))
            .Where(x => !reachable.Contains(x.Ability.Unlock)
                        || !MysteryPhases.RolesRevealed(x.Ability.Unlock))
            .Select(x => $"{x.Faction.Name}: {x.Ability.Name} unlocks in {x.Ability.Unlock}")
            .ToList();
    }
}
