using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// Who won.
///
/// Killers win if two of the three survive all three trials; town wins if two of the three are
/// convicted. Across the conviction slots those are exhaustive and mutually exclusive, so there is
/// no outcome where nobody wins.
///
/// Everything here reads ground truth. A minion's shield or decoy changes what the room believes on
/// a conviction card and nothing else — the whole point of the ability is that it fools people
/// rather than the game.
/// </summary>
public static class OutcomeService
{
    /// <summary>
    /// Work out the ending and write it down, so the reveal screen is not recomputing a verdict
    /// while twenty-five people watch.
    /// </summary>
    public static MysteryOutcome End(TripData trip, DateTimeOffset now)
    {
        var killers = trip.Mystery.Story.Killers.ToList();
        var convicted = trip.Mystery.Play.ConvictedCharacterIds.ToHashSet(StringComparer.Ordinal);
        var caught = killers.Count(k => convicted.Contains(k.Id));

        var outcome = new MysteryOutcome
        {
            KillersConvicted = caught,

            // Two of three. Below that, enough of the Syndicate walked out to burn the evidence.
            TownWon = caught >= 2,
            PersonalWinnerCharacterIds = PersonalWinners(trip).ToList(),
            EndedAt = now
        };

        trip.Mystery.Play.Outcome = outcome;
        return outcome;
    }

    /// <summary>
    /// The quiet wins, which are never announced until the end.
    ///
    /// A jester scores by being convicted; a claimant scores by outliving a convicted rival. Both
    /// are silent and neither ends anything — announcing a jester's win mid-game would end the fun
    /// of it, and it would also hand the room a confirmed innocent for free.
    /// </summary>
    public static IEnumerable<string> PersonalWinners(TripData trip)
    {
        var convicted = trip.Mystery.Play.ConvictedCharacterIds.ToHashSet(StringComparer.Ordinal);

        foreach (var character in trip.Mystery.Story.Guests)
        {
            var won = character.FactionId switch
            {
                "jester" => convicted.Contains(character.Id),

                "inheritance" =>
                    character.RivalCharacterId is { } rival
                    && convicted.Contains(rival)
                    && !convicted.Contains(character.Id),

                _ => false
            };

            if (won) yield return character.Id;
        }
    }

    /// <summary>
    /// Everybody on the winning side, for the last screen of the night.
    ///
    /// The killers' associates win with them. Town's win is the detectives' and the villagers'
    /// together — they were doing the same job with different tools.
    /// </summary>
    public static IReadOnlyList<string> Winners(TripData trip)
    {
        var outcome = trip.Mystery.Play.Outcome;
        if (outcome is null) return [];

        var sides = outcome.TownWon
            ? new[] { "detective", "villager" }
            : ["killer", "minion"];

        return trip.Mystery.Story.Guests
            .Where(c => sides.Contains(c.FactionId))
            .Select(c => c.Id)
            .Concat(outcome.PersonalWinnerCharacterIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The closing stats. Cheap, and the part people talk about afterwards.</summary>
    public static (string? MostMet, string? LeastMet, (string, string)? NeverSpoke) PartyStats(TripData trip)
    {
        var ranked = ScanShareService.Underserved(trip);
        if (ranked.Count == 0) return (null, null, null);

        var leastMet = ranked[0].Character.Id;
        var mostMet = ranked[^1].Character.Id;

        // The pair the evening never introduced. There are usually several; the first is enough.
        var guests = trip.Mystery.Story.Guests.Select(c => c.Id).ToList();
        (string, string)? neverSpoke = null;

        for (var i = 0; i < guests.Count && neverSpoke is null; i++)
            for (var j = i + 1; j < guests.Count; j++)
                if (!ScanShareService.HasMet(trip, guests[i], guests[j]))
                {
                    neverSpoke = (guests[i], guests[j]);
                    break;
                }

        return (mostMet, leastMet, neverSpoke);
    }
}
