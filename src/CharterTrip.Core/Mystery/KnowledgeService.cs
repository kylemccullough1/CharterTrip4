using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// Who is told about whom.
///
/// This lived on the phone page for about an hour, which was a mistake: it is the rule that decides
/// whether three people spend the evening knowing each other's names, and it cannot be checked by
/// looking at a screen. Killers know their associates and are known by them; detectives know each
/// other; the two claimants know their rival. Jesters know nobody at all, and are quietly in
/// competition for the same conviction slot.
/// </summary>
public static class KnowledgeService
{
    /// <summary>
    /// The people this character is told they are working with.
    ///
    /// Empty until roles drop, and empty for anybody whose faction does not know itself. Reads
    /// <c>KnowsEachOther</c> off the faction rather than hard-coding the list, so a story that
    /// changes its mind about jesters is one field away.
    /// </summary>
    public static IReadOnlyList<MysteryCharacter> AlliesFor(TripData trip, string characterId)
    {
        if (!PhaseService.RolesRevealed(trip)) return [];

        var story = trip.Mystery.Story;
        var me = story.Character(characterId);
        if (me is null || me.IsStaff) return [];

        if (story.Faction(me.FactionId) is not { KnowsEachOther: true }) return [];

        // The claim is a pair, not a team. Each is told exactly one name — their rival's — and
        // the pairing is stored both ways so a briefing and the reveal cannot disagree.
        if (me.FactionId == "inheritance")
            return me.RivalCharacterId is { } rival && story.Character(rival) is { } r ? [r] : [];

        var sides = SidesKnownTo(me.FactionId);

        return story.Guests
            .Where(c => c.Id != me.Id && sides.Contains(c.FactionId))
            .ToList();
    }

    /// <summary>
    /// The knowledge is deliberately asymmetric.
    ///
    /// Killers are given their associates; associates are given the killers but not each other. A
    /// caught associate who could name the other one would hand the room two convictions for the
    /// price of one, and the associates' whole job is to be individually deniable.
    /// </summary>
    private static string[] SidesKnownTo(string factionId) => factionId switch
    {
        "killer" => ["killer", "minion"],
        "minion" => ["killer"],
        _ => [factionId]
    };
}
