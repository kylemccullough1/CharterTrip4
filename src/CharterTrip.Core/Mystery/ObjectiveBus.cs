using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// What the game asks people to do, and how it reaches their phones.
///
/// A producer/consumer queue, one-way. Producers are the phase machine — which fires a phase's own
/// objectives as it enters it — and any of the four staff, who can aim one at everybody, at a
/// faction, or at named individuals from their Control tab. Consumers are twenty-five phones, each
/// reading only what is addressed to it. Nothing travels back up except "done".
///
/// One record per publication rather than one per recipient. An objective to twenty-one people is
/// one sentence, not twenty-one copies of it in a document that gets rewritten on every vote.
///
/// Delivery needs no code at all: the store's Changed event already fans out to every open circuit,
/// so publishing inside a mutation is the whole of it.
/// </summary>
public static class ObjectiveBus
{
    // ------------------------------------------------------------------------------------------
    //  Producing
    // ------------------------------------------------------------------------------------------

    /// <summary>Put something on the queue.</summary>
    public static MysteryObjectiveIssue Publish(TripData trip, MysteryObjectiveIssue objective)
    {
        if (string.IsNullOrWhiteSpace(objective.Id))
            objective.Id = Guid.NewGuid().ToString("n")[..12];

        trip.Mystery.Play.Objectives.Add(objective);
        return objective;
    }

    /// <summary>
    /// Fire everything a phase issues on its own.
    ///
    /// Idempotent per template and phase, so re-entering a phase — which the skip strip makes easy —
    /// does not hand everybody the same instruction twice.
    /// </summary>
    public static void PublishForPhase(TripData trip, MysteryPhase phase, DateTimeOffset now)
    {
        foreach (var template in trip.Mystery.Story.Objectives.Where(o => o.Phase == phase))
        {
            var already = trip.Mystery.Play.Objectives.Any(o =>
                o.TemplateId == template.Id && o.IssuedInPhase == phase);

            if (already) continue;

            Publish(trip, FromTemplate(template, phase, now, issuedBy: null));
        }
    }

    /// <summary>
    /// A staff member sending something themselves.
    ///
    /// <paramref name="fills"/> replaces the template's <c>{slots}</c> — the console offers a picker
    /// per slot. Anything left unfilled stays as written rather than becoming an empty gap, so a
    /// half-completed send reads as obviously unfinished instead of subtly wrong.
    /// </summary>
    public static MysteryObjectiveIssue Send(
        TripData trip,
        MysteryObjectiveTemplate template,
        MysteryPhase phase,
        DateTimeOffset now,
        string issuedByPersonId,
        IReadOnlyDictionary<string, string>? fills = null,
        MysteryAudience? audience = null,
        string? factionId = null,
        IEnumerable<string>? characterIds = null)
    {
        var issue = FromTemplate(template, phase, now, issuedByPersonId, fills);

        if (audience is { } a) issue.Audience = a;
        if (factionId is not null) issue.FactionId = factionId;
        if (characterIds is not null) issue.CharacterIds = characterIds.ToList();

        return Publish(trip, issue);
    }

    /// <summary>Free text, for the thing nobody wrote a template for.</summary>
    public static MysteryObjectiveIssue SendFreeText(
        TripData trip,
        string text,
        MysteryPhase phase,
        DateTimeOffset now,
        string issuedByPersonId,
        MysteryAudience audience,
        string? factionId = null,
        IEnumerable<string>? characterIds = null) =>
        Publish(trip, new MysteryObjectiveIssue
        {
            Text = text,
            Audience = audience,
            FactionId = factionId,
            CharacterIds = characterIds?.ToList() ?? [],
            IssuedInPhase = phase,
            IssuedByPersonId = issuedByPersonId,
            IssuedAt = now
        });

    // ------------------------------------------------------------------------------------------
    //  Consuming
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Everything currently asked of one character, newest first.
    ///
    /// Objectives stack rather than replace. A facilitator's nudge arriving does not silently wipe
    /// the instruction somebody was halfway through, and the tab is an honest list of what the game
    /// wants from you rather than a single line that keeps changing.
    /// </summary>
    public static IReadOnlyList<MysteryObjectiveIssue> Inbox(TripData trip, string characterId)
    {
        var factionId = trip.Mystery.Story.Character(characterId)?.FactionId;
        var rolesOut = PhaseService.RolesRevealed(trip);

        return trip.Mystery.Play.Objectives
            .Where(o => Addresses(o, characterId, factionId, rolesOut))
            .OrderByDescending(o => o.IssuedAt)
            .ToList();
    }

    /// <summary>The ones they have not ticked off. What the tab badges.</summary>
    public static IReadOnlyList<MysteryObjectiveIssue> Outstanding(TripData trip, string characterId) =>
        Inbox(trip, characterId).Where(o => !o.CompletedBy.Contains(characterId)).ToList();

    public static bool Complete(TripData trip, string characterId, string objectiveId)
    {
        var objective = trip.Mystery.Play.Objectives.FirstOrDefault(o => o.Id == objectiveId);
        if (objective is null || objective.CompletedBy.Contains(characterId)) return false;

        objective.CompletedBy.Add(characterId);
        return true;
    }

    public static bool Reopen(TripData trip, string characterId, string objectiveId)
    {
        var objective = trip.Mystery.Play.Objectives.FirstOrDefault(o => o.Id == objectiveId);
        return objective is not null && objective.CompletedBy.Remove(characterId);
    }

    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether this objective is for this character.
    ///
    /// The faction arm is gated on roles having dropped, which matters more than it looks: a
    /// faction-addressed objective published early would tell somebody they are a detective by the
    /// simple fact of arriving.
    /// </summary>
    private static bool Addresses(
        MysteryObjectiveIssue objective, string characterId, string? factionId, bool rolesRevealed) =>
        objective.Audience switch
        {
            MysteryAudience.Everyone => true,
            MysteryAudience.Characters => objective.CharacterIds.Contains(characterId),
            MysteryAudience.Faction => rolesRevealed && objective.FactionId == factionId,
            _ => false
        };

    private static MysteryObjectiveIssue FromTemplate(
        MysteryObjectiveTemplate template,
        MysteryPhase phase,
        DateTimeOffset now,
        string? issuedBy,
        IReadOnlyDictionary<string, string>? fills = null) =>
        new()
        {
            TemplateId = template.Id,

            // Copied in, not referenced. Rewriting a template at nine o'clock must not change what
            // somebody was told at half past eight.
            Text = Fill(template.Text, fills),

            Audience = template.Audience,
            FactionId = template.FactionId,
            IssuedInPhase = phase,
            IssuedByPersonId = issuedBy,
            IssuedAt = now
        };

    private static string Fill(string text, IReadOnlyDictionary<string, string>? fills)
    {
        if (fills is null || fills.Count == 0) return text;

        foreach (var (slot, value) in fills)
            text = text.Replace("{" + slot + "}", value, StringComparison.Ordinal);

        return text;
    }
}
