namespace CharterTrip.Core.Mystery.Script;

/// <summary>
/// Who everyone secretly is, from <c>factions.json</c>.
/// </summary>
public sealed record ScriptFactionBook
{
    public IReadOnlyList<ScriptFaction> Factions { get; init; } = [];

    /// <summary>What happens to a player after they are convicted. Ghosts are not a faction —
    /// they are a state every faction can end up in.</summary>
    public ScriptGhostRules GhostRules { get; init; } = new();

    public ScriptFaction? ById(string id) => Factions.FirstOrDefault(f => f.Id == id);

    /// <summary>Must equal 21. Asserted by the script test, because a faction table that does not
    /// account for every guest means somebody has no role on the night.</summary>
    public int TotalSeats => Factions.Sum(f => f.Count);
}

/// <summary>
/// One faction: how many, what they know, what winning means, and what they can do about it.
/// </summary>
public sealed record ScriptFaction
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Count { get; init; }

    /// <summary>How the generator fills these seats, in prose. The authority is
    /// <c>README.md</c>'s algorithm; this is the human-readable version of the same rules.</summary>
    public string Assignment { get; init; } = "";

    /// <summary>What members of this faction are told about each other.</summary>
    public string Knowledge { get; init; } = "";

    /// <summary>The win condition. Ruleset B: killers on 2+ of 3 surviving, town on 2+ convicted.</summary>
    public string Win { get; init; } = "";

    public string? WinNote { get; init; }

    public IReadOnlyList<ScriptAbility> Abilities { get; init; } = [];
}

/// <summary>
/// A thing a player can do, once or twice, after it unlocks.
///
/// An ability is either a single effect (<see cref="Text"/>) or a choice between named modes
/// (<see cref="Modes"/>) — plant or scrub, shield or decoy, subtle or blatant. The mode is picked
/// at the moment of use and cannot be taken back, which is most of what makes those abilities
/// interesting.
/// </summary>
public sealed record ScriptAbility
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Uses available. For a <see cref="Shared"/> ability this is the whole faction's total.</summary>
    public int Charges { get; init; }

    /// <summary>True if the charges belong to the faction rather than the player — the case that
    /// races when two members fire at the same moment.</summary>
    public bool Shared { get; init; }

    /// <summary>When this becomes available: a round id, or "after_trial_1", or "roles_drop".</summary>
    public string Unlock { get; init; } = "";

    /// <summary>The single effect, for abilities without modes.</summary>
    public string? Text { get; init; }

    /// <summary>Mode id to effect text, for abilities that make the player choose.</summary>
    public IReadOnlyDictionary<string, string>? Modes { get; init; }

    /// <summary>Adjudication detail — what the screen announces, what forensics sees.</summary>
    public string? Notes { get; init; }

    public bool HasModes => Modes is { Count: > 0 };
}

/// <summary>
/// Being dead, and what it still lets you do. Canned messages only — the one hard rule in the
/// whole design, because a dead killer who can type is a dead game.
/// </summary>
public sealed record ScriptGhostRules
{
    public string OnConviction { get; init; } = "";
    public IReadOnlyList<ScriptAbility> Abilities { get; init; } = [];
}
