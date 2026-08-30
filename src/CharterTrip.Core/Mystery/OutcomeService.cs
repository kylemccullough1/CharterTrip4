using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// Who won.
///
/// Killers win if two of the three survive all three trials; town wins if two of the three are
/// convicted. Across the conviction slots those are exhaustive and mutually exclusive, so there is
/// no outcome where nobody wins.
///
/// Everything here reads ground truth. A minion who took the blame changes what the room believes
/// on a conviction card and nothing else — the whole point of the ability is that it fools people
/// rather than the game. The one concession is <see cref="MysteryOutcome.RevealDecoy"/>: when the
/// room would have won on what it was told and did not win on the truth, the ending owes it an
/// explanation.
/// </summary>
public static class OutcomeService
{
    /// <summary>
    /// Work out the ending and write it down, so the reveal screen is not recomputing a verdict
    /// while twenty-five people watch.
    /// </summary>
    public static MysteryOutcome End(TripData trip, DateTimeOffset now)
    {
        var outcome = Compute(trip, now);
        trip.Mystery.Play.Outcome = outcome;
        return outcome;
    }

    /// <summary>
    /// The ending, whether or not anybody has written it down yet.
    ///
    /// Every screen reads here rather than at <c>Play.Outcome</c>. Two ways to arrive at the reveal
    /// with nothing recorded: a game saved before the phase machine settled it, and the host jumping
    /// straight to Reveal from the skip strip in a game that had already been there. Both used to
    /// headline the last screen of the night with an ellipsis and name nobody as a winner.
    /// </summary>
    public static MysteryOutcome Current(TripData trip) =>
        trip.Mystery.Play.Outcome ?? Compute(trip, trip.Mystery.Play.MurderAt ?? default);

    public static MysteryOutcome Compute(TripData trip, DateTimeOffset now)
    {
        var killers = trip.Mystery.Story.Killers.ToList();
        var convicted = trip.Mystery.Play.ConvictedCharacterIds.ToHashSet(StringComparer.Ordinal);
        var caught = killers.Count(k => convicted.Contains(k.Id));
        var shown = TrialService.ShownKillersConvicted(trip);

        // Two of three. Below that, enough of the Syndicate walked out to burn the evidence.
        const int needed = 2;

        return new MysteryOutcome
        {
            KillersConvicted = caught,
            ShownKillersConvicted = shown,
            TownWon = caught >= needed,

            // The room thought it had them and it did not: one of the KILLER cards was an
            // associate. Anything else — a real win, or a loss that was a loss either way — and
            // the associate is never named as one.
            RevealDecoy = shown >= needed && caught < needed,
            PersonalWinnerCharacterIds = PersonalWinners(trip).ToList(),
            EndedAt = now
        };
    }

    /// <summary>The minions who took the blame and went down for it, for the ending to name.</summary>
    public static IReadOnlyList<MysteryCharacter> ConvictedDecoys(TripData trip) =>
        trip.Mystery.Play.ConvictedCharacterIds
            .Where(id => TrialService.TookTheBlame(trip, id))
            .Select(id => trip.Mystery.Story.Character(id))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

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
        var outcome = Current(trip);

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

    /// <summary>
    /// One card of the ending, in the order the reveal walks them: the winners, and only the
    /// winners.
    ///
    /// Everything the room was never allowed to know, gathered once so the last screen of the night
    /// is reading a list rather than recomputing a verdict twenty-one times while everybody watches.
    /// Everybody with a role gets a card of their own — who they were, why they did it — and the
    /// guests of the house, when they win, get one card between them. Twenty-one faces one at a
    /// time is a reveal nobody watches to the end.
    /// </summary>
    public sealed record RevealCard(
        IReadOnlyList<MysteryCharacter> Characters,
        MysteryFaction? Faction,
        string? Blurb,
        bool Convicted,
        IReadOnlyList<string> Deeds,
        bool Group)
    {
        /// <summary>The one person on a single card. The first of them on the villagers' card.</summary>
        public MysteryCharacter Character => Characters[0];
    }

    /// <summary>Factions in the order their winners are read out. Villagers come last, together.</summary>
    private static readonly string[] RevealOrder = ["killer", "minion", "detective", "jester", "inheritance"];

    public const string VillagerFactionId = "villager";

    public static IReadOnlyList<RevealCard> Reveal(TripData trip)
    {
        var story = trip.Mystery.Story;
        var convicted = trip.Mystery.Play.ConvictedCharacterIds.ToHashSet(StringComparer.Ordinal);

        var winners = Winners(trip)
            .Select(story.Character)
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

        var cards = winners
            .Where(c => c.FactionId != VillagerFactionId)
            .OrderBy(c => Array.IndexOf(RevealOrder, c.FactionId) is var i and >= 0 ? i : RevealOrder.Length)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new RevealCard(
                [c],
                story.Faction(c.FactionId),
                MysteryText.Written(c.Epilogue),
                convicted.Contains(c.Id),
                Deeds(trip, c.Id),
                Group: false))
            .ToList();

        var villagers = winners.Where(c => c.FactionId == VillagerFactionId).ToList();
        if (villagers.Count > 0)
            cards.Add(new RevealCard(
                villagers,
                story.Faction(VillagerFactionId),
                MysteryText.Written(story.Beats.VillagerEpilogue),
                Convicted: false,
                Deeds: [],
                Group: true));

        return cards;
    }

    /// <summary>
    /// What this person did that nobody saw, in sentences.
    ///
    /// The tamper log is the good half of this: "a clue has been tampered with" is all the room ever
    /// got told, and the whole evening is spent arguing about which one. Naming it at the end is the
    /// payoff for two hours of suspicion.
    /// </summary>
    private static IReadOnlyList<string> Deeds(TripData trip, string characterId)
    {
        var story = trip.Mystery.Story;
        var deeds = new List<string>();

        foreach (var state in trip.Mystery.Play.ClueStates.Where(s => s.Tamper?.ByCharacterId == characterId))
        {
            var room = story.Zone(story.Clue(state.ClueId)?.ZoneId ?? "")?.Name ?? "a room";
            var framed = story.Character(state.Tamper!.TargetCharacterId ?? "")?.Name;

            deeds.Add(state.Tamper.Mode switch
            {
                "scrub" => $"Wiped the card in the {room}.",
                _ when framed is null => $"Worked on the card in the {room}.",
                _ when framed == story.Character(characterId)?.Name => $"Planted their own belongings on the card in the {room}.",
                _ => $"Framed {framed} on the card in the {room}."
            });
        }

        foreach (var use in trip.Mystery.Play.AbilityUses.Where(u => u.ByCharacterId == characterId))
        {
            // Tampering already has its own sentence above, and saying it twice reads as two acts.
            if (use.TargetClueId is { Length: > 0 }) continue;

            var ability = story.Faction(use.FactionId)?.Abilities.FirstOrDefault(a => a.Id == use.AbilityId);
            var target = story.Character(use.TargetCharacterId ?? "")?.Name;

            deeds.Add(use.AbilityId switch
            {
                TrialService.BlameAbilityId => "Took the blame for the Syndicate.",
                _ when target is null => $"Used {ability?.Name ?? use.AbilityId}.",
                _ => $"Used {ability?.Name ?? use.AbilityId} on {target}."
            });
        }

        return deeds;
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
