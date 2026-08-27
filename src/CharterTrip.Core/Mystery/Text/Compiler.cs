using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery.Script;

namespace CharterTrip.Core.Mystery.Text;

/// <summary>
/// What one character was seen doing, by one other character.
/// </summary>
/// <param name="AboutCharacterId">Who was seen.</param>
/// <param name="AboutName">Their name, so a caller does not have to look it up to render a list.</param>
/// <param name="Text">The authored observation, in whichever reading they were dealt.</param>
/// <param name="FromNextRoom">True for a cross-zone sighting — seen from the doorway rather than up close.</param>
public sealed record WitnessStatement(string AboutCharacterId, string AboutName, string Text, bool FromNextRoom);

/// <summary>
/// Everything one player reads on their own phone.
/// </summary>
public sealed record PlayerBriefing(
    string CharacterId,
    string Name,
    string Title,
    string Motive,
    string Fear,
    string FactionId,
    string FactionName,
    string ZoneName,
    string? GuiltSlot,
    string? KillerBriefing,
    string? CoverStory,
    string? RivalName,
    IReadOnlyList<WitnessStatement> Witnessed);

/// <summary>
/// Composes every sentence the game says, out of the authored blocks and the deal.
///
/// <c>assembly_rules</c> in <c>story_beats.json</c> is the specification and this is a literal
/// implementation of it. Nothing here writes prose: it substitutes names into blocks somebody else
/// wrote, which is what makes a game with millions of distinct deals reviewable in one sitting.
///
/// Pure — the same script and deal always compose the same words. That is what lets a seed be
/// replayed and a briefing be checked without a phone.
///
/// A placeholder with no authored source is left visible rather than guessed at. If
/// <c>{result_line}</c> shows up on a screen, the content is missing a fragment and the fix is to
/// write it, not to have this file invent one.
/// </summary>
public static class Compiler
{
    /// <summary>
    /// The study as the room finds it: the fixed scene, plus the flavour of how it was actually
    /// done. <c>study_scene = study_scene_base + method_beats[means_killer].scene_flavor</c>.
    /// </summary>
    public static string StudyScene(MysteryScript script, MysteryDeal deal)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(deal);

        var means = deal.KillerFor("means");
        var flavour = means is not null && script.StoryBeats.MethodBeats.TryGetValue(means, out var beat)
            ? beat.SceneFlavor
            : "";

        return Fill(script.StoryBeats.Spine.StudySceneBase, new() { ["method_scene_flavor"] = flavour }).Trim();
    }

    /// <summary>What the screen says when the body is found. Fixed in every game.</summary>
    public static string MurderAnnouncement(MysteryScript script) =>
        script.StoryBeats.Spine.MurderAnnouncement;

    /// <summary>The intro for a round, if it has one.</summary>
    public static string? RoundIntro(MysteryScript script, string roundId) =>
        script.StoryBeats.Spine.RoundIntros.TryGetValue(roundId, out var text) ? text : null;

    /// <summary>
    /// Everything on one player's phone.
    /// </summary>
    public static PlayerBriefing? BriefingFor(MysteryScript script, MysteryDeal deal, string characterId)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(deal);

        var member = deal.Cast.FirstOrDefault(c => c.CharacterId == characterId);
        var character = script.CharacterById(characterId);
        if (member is null || character is null) return null;

        var faction = script.Factions.ById(member.FactionId);
        var rival = member.RivalCharacterId is { } rivalId ? script.CharacterById(rivalId) : null;

        return new PlayerBriefing(
            CharacterId: characterId,
            Name: character.Name,
            Title: character.Title,
            Motive: character.Motive,
            Fear: character.Fear,
            FactionId: member.FactionId,
            FactionName: faction?.Name ?? "",
            ZoneName: script.Zones.ById(member.ZoneId)?.Name ?? "",
            GuiltSlot: member.GuiltSlot,
            KillerBriefing: KillerBriefing(script, deal, member),
            CoverStory: CoverStory(script, deal, member),
            RivalName: rival?.Name,
            Witnessed: WitnessStatementsFor(script, deal, characterId));
    }

    /// <summary>
    /// A killer's own account of what they did, per the three
    /// <c>killer_briefing_*</c> rules. Null for everybody else — including red herrings, who
    /// genuinely do not know why the room is looking at them.
    /// </summary>
    public static string? KillerBriefing(MysteryScript script, MysteryDeal deal, MysteryCastMember member)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(deal);
        ArgumentNullException.ThrowIfNull(member);

        return member.GuiltSlot switch
        {
            "access" => script.StoryBeats.AccessBeats.TryGetValue(deal.AccessRoute, out var access)
                ? access.Briefing
                : null,

            "means" => script.StoryBeats.MethodBeats.TryGetValue(member.CharacterId, out var method)
                ? method.Briefing
                : null,

            "signature" => script.StoryBeats.SignatureBeats.Briefing,

            _ => null
        };
    }

    /// <summary>
    /// Who a killer points at when pressed, and what they claim to have seen them do.
    ///
    /// Each killer is paired with one red herring — three of each, so one apiece. The pairing is
    /// derived rather than stored, from the slot order and the herrings sorted by id, so it is
    /// stable for a given deal however the cast list happens to be ordered.
    /// </summary>
    public static string? CoverStory(MysteryScript script, MysteryDeal deal, MysteryCastMember member)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(deal);
        ArgumentNullException.ThrowIfNull(member);

        if (member.GuiltSlot is null) return null;

        var herring = HerringFor(deal, member.GuiltSlot);
        if (herring is null) return null;

        var character = script.CharacterById(herring.CharacterId);
        if (character is null) return null;

        return Fill(script.StoryBeats.AssemblyRules.CoverStoryTemplate, new()
        {
            ["herring_name"] = character.Name,
            ["herring_seen_guilty"] = character.Seen.Guilty
        });
    }

    /// <summary>
    /// What this character saw, per the <c>witness_statements</c> rule: two to three co-located
    /// characters, each in the reading they were dealt.
    ///
    /// The cap is what makes the web solvable rather than exhaustive — a room of five would
    /// otherwise hand one player four statements and drown the signal. Cross-zone sightings are
    /// appended on top, flagged, because "seen from the doorway" is weaker evidence and the player
    /// should be able to say so.
    /// </summary>
    public static IReadOnlyList<WitnessStatement> WitnessStatementsFor(
        MysteryScript script, MysteryDeal deal, string characterId)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(deal);

        var member = deal.Cast.FirstOrDefault(c => c.CharacterId == characterId);
        if (member is null) return [];

        var statements = new List<WitnessStatement>();

        // Deterministic order so a briefing reads the same every time it is composed.
        var coLocated = deal.Cast
            .Where(c => c.ZoneId == member.ZoneId && c.CharacterId != characterId)
            .OrderBy(c => c.CharacterId, StringComparer.Ordinal)
            .Take(3);

        foreach (var other in coLocated)
        {
            var character = script.CharacterById(other.CharacterId);
            if (character is null) continue;

            statements.Add(new WitnessStatement(
                other.CharacterId,
                character.Name,
                character.Seen.For(other.ShowsGuilty),
                FromNextRoom: false));
        }

        foreach (var sighting in deal.CrossZoneSightings.Where(s => s.ObserverCharacterId == characterId))
        {
            var subject = deal.Cast.FirstOrDefault(c => c.CharacterId == sighting.SubjectCharacterId);
            var character = script.CharacterById(sighting.SubjectCharacterId);
            if (subject is null || character is null) continue;

            statements.Add(new WitnessStatement(
                subject.CharacterId,
                character.Name,
                character.Seen.For(subject.ShowsGuilty),
                FromNextRoom: true));
        }

        return statements;
    }

    /// <summary>
    /// What a clue card reads right now — authored trace, or tampered, or scrubbed.
    ///
    /// A room without a trace shows the zone's own <c>clue_spot</c> line. The content has no
    /// authored text for neutral clues, and describing where the card sits is better than putting
    /// words in the author's mouth.
    /// </summary>
    public static string ClueText(MysteryScript script, MysteryClue clue)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(clue);

        var tamper = script.StoryBeats.TamperSystem;

        // A scrub replaces the reading entirely rather than adding to it.
        if (clue.Tamper is { Mode: "scrub" }) return tamper.ScrubRender;

        var baseText = clue.TraceCharacterId is { } traceId
            ? script.CharacterById(traceId)?.Trace.Text ?? ""
            : script.Zones.ById(clue.ZoneId)?.ClueSpot ?? "";

        if (clue.Tamper is not { } t) return baseText;

        var render = tamper.RenderFor(t.Mode);
        if (render is null || t.TargetCharacterId is null) return baseText;

        var insert = script.CharacterById(t.TargetCharacterId)?.TamperInsert ?? "";

        return $"{baseText} {Fill(render, new() { ["tamper_insert"] = insert })}".Trim();
    }

    /// <summary>The name of the thing on the card, for a feed that lists clues by name.</summary>
    public static string ClueName(MysteryScript script, MysteryClue clue)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(clue);

        return clue.TraceCharacterId is { } traceId
            ? script.CharacterById(traceId)?.Trace.Name ?? ""
            : script.Zones.ById(clue.ZoneId)?.Name ?? "";
    }

    /// <summary>What forensics reports about a clue. Reveals the original alongside the current.</summary>
    public static string Forensics(MysteryScript script, MysteryClue clue)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(clue);

        var tamper = script.StoryBeats.TamperSystem;
        var name = ClueName(script, clue);

        if (clue.Tamper is null)
            return Fill(tamper.ForensicsClean, new() { ["clue_name"] = name });

        var original = clue.TraceCharacterId is { } traceId
            ? script.CharacterById(traceId)?.Trace.Text ?? ""
            : script.Zones.ById(clue.ZoneId)?.ClueSpot ?? "";

        return Fill(tamper.ForensicsResult, new()
        {
            ["clue_name"] = name,
            ["original_text"] = original
        });
    }

    /// <summary>
    /// The card the main screen shows when somebody is convicted.
    ///
    /// Killers read as KILLER unless a minion took the blame; everybody else reads as GUEST,
    /// because revealing "detective" mid-game hands the killers a kill list for free.
    /// </summary>
    public static string ConvictionCard(
        MysteryScript script, MysteryDeal deal, string characterId, bool blameTaken)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(deal);

        var member = deal.Cast.FirstOrDefault(c => c.CharacterId == characterId);
        var character = script.CharacterById(characterId);
        if (member is null || character is null) return "";

        var key = member switch
        {
            { IsKiller: true } when blameTaken => "killer_blamed",
            { IsKiller: true } => "killer",
            { FactionId: "minion" } => "minion",
            _ => "guest"
        };

        var template = script.StoryBeats.ConvictionReveals.GetValueOrDefault(key, "");
        return Fill(template, new() { ["name"] = character.Name });
    }

    /// <summary>
    /// The endgame, composed from whichever branches apply.
    ///
    /// Returns the paragraphs in reading order. Ruleset B decides the first one: town on 2+ killers
    /// convicted, killers on 2+ surviving. A town win reads as all three caught, because two in
    /// custody means the third is rolled up off the back of them.
    /// </summary>
    public static IReadOnlyList<string> Endgame(MysteryScript script, MysteryState state)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(state);

        if (state.Deal is not { } deal) return [];

        var beats = script.StoryBeats;
        var convicted = state.ConvictedCharacterIds.ToHashSet(StringComparer.Ordinal);
        var killersConvicted = deal.Killers.Count(k => convicted.Contains(k.CharacterId));
        var townWon = killersConvicted >= 2;

        var paragraphs = new List<string>();

        var access = deal.KillerFor("access");
        var means = deal.KillerFor("means");
        var signature = deal.KillerFor("signature");

        var survivors = deal.Killers
            .Where(k => !convicted.Contains(k.CharacterId))
            .Select(k => script.CharacterById(k.CharacterId)?.Name ?? k.CharacterId)
            .ToList();

        var fills = new Dictionary<string, string>
        {
            ["killer_access"] = Name(script, access),
            ["access_route_reveal"] = script.Zones.AccessRoutes.TryGetValue(deal.AccessRoute, out var route)
                ? route.Label
                : "",
            ["method_reveal"] = means is not null && beats.MethodBeats.TryGetValue(means, out var method)
                ? Fill(method.Reveal, new() { ["name"] = Name(script, means) })
                : "",
            ["signature_reveal"] = signature is not null
                ? Fill(beats.SignatureBeats.Reveal, new() { ["name"] = Name(script, signature) })
                : "",
            ["surviving_killers"] = Join(survivors)
        };

        paragraphs.Add(Fill(beats.EndgameReveals.GetValueOrDefault(townWon ? "town_win" : "killer_win", ""), fills));

        // The investigators, then the associates. Both are named regardless of who won.
        var detectives = NamesIn(script, deal, "detective");
        if (detectives.Count > 0)
            paragraphs.Add(Fill(beats.EndgameReveals.GetValueOrDefault("detective_reveal", ""),
                new() { ["names"] = Join(detectives) }));

        var minions = NamesIn(script, deal, "minion");
        if (minions.Count > 0)
            paragraphs.Add(Fill(beats.EndgameReveals.GetValueOrDefault("minion_reveal", ""),
                new() { ["names"] = Join(minions) }));

        // Jesters: convicted is a win, and it is silent until now.
        foreach (var jester in deal.InFaction("jester"))
        {
            var character = script.CharacterById(jester.CharacterId);
            if (character is null) continue;

            var key = convicted.Contains(jester.CharacterId) ? "jester_reveal" : "jester_fail";

            paragraphs.Add(Fill(beats.EndgameReveals.GetValueOrDefault(key, ""), new()
            {
                ["name"] = character.Name,
                ["fear_line"] = character.Fear
            }));
        }

        // The Braun claim: rival convicted and you survived. Both, or neither wins.
        var claimants = deal.InFaction("inheritance").ToList();
        var claimWinner = claimants.FirstOrDefault(c =>
            c.RivalCharacterId is { } rival &&
            convicted.Contains(rival) &&
            !convicted.Contains(c.CharacterId));

        paragraphs.Add(claimWinner is not null
            ? Fill(beats.EndgameReveals.GetValueOrDefault("braun_win", ""), new()
            {
                ["winner"] = Name(script, claimWinner.CharacterId),
                ["loser"] = Name(script, claimWinner.RivalCharacterId)
            })
            : beats.EndgameReveals.GetValueOrDefault("braun_none", ""));

        // Every red herring gets their name cleared, which is what makes burning one feel fair.
        foreach (var herring in deal.Herrings)
        {
            var character = script.CharacterById(herring.CharacterId);
            if (character is null) continue;

            paragraphs.Add(Fill(beats.AssemblyRules.HerringExoneration, new()
            {
                ["name"] = character.Name,
                ["herring_truth"] = character.HerringTruth
            }));
        }

        // A paragraph with an unfilled placeholder is content that does not exist yet, and a
        // visible {result_line} on the wall is worse than the paragraph being absent. Dropped
        // here and reported by MissingFragments, so the gap is fixable rather than invisible.
        return [.. paragraphs.Where(p => p.Length > 0 && !HasHole(p))];
    }

    /// <summary>
    /// Placeholders the content has no source for, as <c>template → placeholder</c>.
    ///
    /// Empty means every endgame branch composes fully. Anything listed here is a fragment somebody
    /// needs to write — the compiler will not guess at it, and <see cref="Endgame"/> silently omits
    /// the paragraph until it exists. Surfaced on the host console so it is noticed before the
    /// night rather than during the reveal.
    ///
    /// Known gap today: <c>detective_reveal</c> wants a <c>{result_line}</c> that nothing in
    /// <c>story_beats.json</c> provides.
    /// </summary>
    public static IReadOnlyList<string> MissingFragments(MysteryScript script, MysteryState state)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(state);

        if (state.Deal is null) return [];

        var gaps = new List<string>();

        // Compose every branch and look at what is left over. Doing it by composition rather than
        // by a hand-kept list means a newly added template cannot be forgotten here.
        foreach (var (key, template) in script.StoryBeats.EndgameReveals)
        {
            foreach (var placeholder in Holes(template))
            {
                if (!KnownPlaceholders.Contains(placeholder))
                    gaps.Add($"{key} → {{{placeholder}}}");
            }
        }

        return gaps;
    }

    /// <summary>Every placeholder <see cref="Endgame"/> knows how to fill.</summary>
    private static readonly HashSet<string> KnownPlaceholders = new(StringComparer.Ordinal)
    {
        "killer_access", "access_route_reveal", "method_reveal", "signature_reveal",
        "surviving_killers", "name", "fear_line", "winner", "loser", "names",
        "winner_list", "herring_truth", "clue_name", "original_text", "tamper_insert"
    };

    private static bool HasHole(string text) => Holes(text).Any();

    private static IEnumerable<string> Holes(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;

            var close = text.IndexOf('}', i + 1);
            if (close < 0) break;

            yield return text[(i + 1)..close];
            i = close;
        }
    }

    /// <summary>
    /// Who won, on ground truth. Blame-take fools the reveal card, never this.
    /// </summary>
    public static MysteryOutcome Outcome(MysteryState state, DateTimeOffset endedAt)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Deal is not { } deal)
            return new MysteryOutcome { EndedAt = endedAt };

        var convicted = state.ConvictedCharacterIds.ToHashSet(StringComparer.Ordinal);
        var killersConvicted = deal.Killers.Count(k => convicted.Contains(k.CharacterId));

        var personal = new List<string>();

        // A jester wins by being convicted. Silent, personal, and it does not end anything.
        personal.AddRange(deal.InFaction("jester")
            .Where(j => convicted.Contains(j.CharacterId))
            .Select(j => j.CharacterId));

        // A claimant wins only with both halves: rival convicted, and they survived.
        personal.AddRange(deal.InFaction("inheritance")
            .Where(c => c.RivalCharacterId is { } rival
                        && convicted.Contains(rival)
                        && !convicted.Contains(c.CharacterId))
            .Select(c => c.CharacterId));

        return new MysteryOutcome
        {
            TownWon = killersConvicted >= 2,
            KillersConvicted = killersConvicted,
            PersonalWinnerCharacterIds = personal,
            EndedAt = endedAt
        };
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>
    /// Which red herring a killer's cover story points at.
    ///
    /// Three killers, three herrings, one each. Paired by slot order against herrings sorted by
    /// id, so the answer depends only on the deal and not on list ordering.
    /// </summary>
    public static MysteryCastMember? HerringFor(MysteryDeal deal, string guiltSlot)
    {
        ArgumentNullException.ThrowIfNull(deal);

        var slots = new[] { "access", "means", "signature" };
        var index = Array.IndexOf(slots, guiltSlot);
        if (index < 0) return null;

        var herrings = deal.Herrings
            .OrderBy(h => h.CharacterId, StringComparer.Ordinal)
            .ToList();

        return index < herrings.Count ? herrings[index] : null;
    }

    private static List<string> NamesIn(MysteryScript script, MysteryDeal deal, string factionId) =>
        [.. deal.InFaction(factionId)
            .Select(m => script.CharacterById(m.CharacterId)?.Name)
            .Where(n => n is not null)
            .Select(n => n!)
            .Order(StringComparer.Ordinal)];

    private static string Name(MysteryScript script, string? characterId) =>
        characterId is null ? "" : script.CharacterById(characterId)?.Name ?? characterId;

    /// <summary>"A", "A and B", "A, B and C" — the way a sentence would say it.</summary>
    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
    };

    /// <summary>
    /// Substitute <c>{placeholder}</c> values.
    ///
    /// A placeholder with no value is left exactly as it was. That is deliberate: a visible
    /// <c>{result_line}</c> on a screen means the content is missing a fragment, which is a thing
    /// to notice and write, not to paper over with a plausible sentence.
    /// </summary>
    private static string Fill(string template, Dictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template)) return "";

        var text = template;
        foreach (var (key, value) in values)
            text = text.Replace($"{{{key}}}", value, StringComparison.Ordinal);

        return text;
    }
}
