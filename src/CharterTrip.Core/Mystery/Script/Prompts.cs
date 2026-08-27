namespace CharterTrip.Core.Mystery.Script;

/// <summary>
/// The nudges that keep quiet players moving, from <c>prompts.json</c>.
///
/// This is the difference between a good party and fifteen people standing near a wall. The three
/// facilitators are the manual version of the same job, and are why the game still works before
/// this is built.
/// </summary>
public sealed record ScriptPromptBook
{
    public ScriptPromptEngine Engine { get; init; } = new();
    public ScriptPromptTemplates Templates { get; init; } = new();
    public ScriptBadgeScanning BadgeScanning { get; init; } = new();
}

/// <summary>
/// How often prompts fire, and which kind wins when several apply.
/// </summary>
public sealed record ScriptPromptEngine
{
    public int CadenceMinutes { get; init; }

    /// <summary>Highest priority first: underserved, contradiction, proximity, clue_linked, faction.
    /// "Underserved" leads deliberately — a player nobody has talked to is the actual failure mode.</summary>
    public IReadOnlyList<string> PriorityOrder { get; init; } = [];

    /// <summary>Silent push to the phone's prompt tray. Never on the main screen. One at a time.</summary>
    public string Delivery { get; init; } = "";

    public string FacilitatorOverride { get; init; } = "";
}

/// <summary>
/// The prompt text itself, grouped by why it is being sent.
/// </summary>
public sealed record ScriptPromptTemplates
{
    public IReadOnlyList<string> Underserved { get; init; } = [];
    public IReadOnlyList<string> Contradiction { get; init; } = [];
    public IReadOnlyList<string> Proximity { get; init; } = [];
    public IReadOnlyList<string> ClueLinked { get; init; } = [];

    /// <summary>Keyed by faction id. These are the only prompts that know who the player is.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Faction { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>The pool for one priority category, or the faction pool for a faction id.</summary>
    public IReadOnlyList<string> For(string category) => category switch
    {
        "underserved" => Underserved,
        "contradiction" => Contradiction,
        "proximity" => Proximity,
        "clue_linked" => ClueLinked,
        _ => Faction.TryGetValue(category, out var pool) ? pool : []
    };
}

/// <summary>
/// Name-tag QR scanning: the interaction graph, pitched to the room as a mingle game.
///
/// It is telemetry, and it is deliberately never explained that way — it feeds underserved
/// detection, contradiction routing, proximity prompts, and the endgame stats.
/// </summary>
public sealed record ScriptBadgeScanning
{
    public string Mechanic { get; init; } = "";
    public IReadOnlyList<string> Feeds { get; init; } = [];
    public string PlayerFraming { get; init; } = "";
}
