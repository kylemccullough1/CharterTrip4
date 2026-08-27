namespace CharterTrip.Core.Mystery.Script;

/// <summary>
/// One of the 21 guests at Braun Manor, exactly as authored in <c>characters.json</c>.
///
/// Every property here is content: read once at startup and never written. The pair fields
/// (<see cref="Acts"/>, <see cref="Seen"/>) are the whole trick of the game — the same character
/// has a guilty reading and an innocent one, and which one the room is shown depends on the deal,
/// not on the character. That is why a red herring is indistinguishable from a killer: it is
/// literally the same text.
/// </summary>
public sealed record ScriptCharacter
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>"M" or "F". Kept because the invitation letters and reveal text read naturally,
    /// not because it constrains casting — it does not.</summary>
    public string Gender { get; init; } = "";

    public string Title { get; init; } = "";

    /// <summary>How they talk. Only an optional AI restyle pass reads this; it may not change facts.</summary>
    public string Voice { get; init; } = "";

    public string Motive { get; init; } = "";
    public string Fear { get; init; } = "";
    public string SignatureItem { get; init; } = "";

    /// <summary>What they physically do during the party, in both readings.</summary>
    public GuiltyInnocent Acts { get; init; } = new();

    /// <summary>What a co-located player reports having seen them do, in both readings.
    /// This is the raw material of every witness statement.</summary>
    public GuiltyInnocent Seen { get; init; } = new();

    /// <summary>The innocent explanation for the guilty reading. Shown at the endgame reveal
    /// and never before — it is what makes a burned red herring feel fair rather than arbitrary.</summary>
    public string HerringTruth { get; init; } = "";

    /// <summary>Their physical clue card, and where it is allowed to be.</summary>
    public ScriptTrace Trace { get; init; } = new();

    /// <summary>The zones this character may be placed in. The generator picks one.</summary>
    public IReadOnlyList<string> Zones { get; init; } = [];

    /// <summary>Which guilt slots this character can fill: access, means, signature. Empty means
    /// they can never be a killer — four characters are deliberately in that position.</summary>
    public IReadOnlyList<string> Slots { get; init; } = [];

    /// <summary>
    /// Set to a faction id to pin this character there before any draw runs.
    ///
    /// Null for all 21 today — the mechanism is kept, but nothing uses it. Harry Braun and Isla
    /// Perry used to be pinned to <c>inheritance</c> in every game; the claim is now drawn from
    /// the non-killer pool like every other faction, and both are ordinary killer candidates.
    /// </summary>
    public string? FixedFaction { get; init; }

    /// <summary>The fragment of themselves a killer can plant on someone else's clue to frame them.</summary>
    public string TamperInsert { get; init; } = "";

    /// <summary>If this character draws the access slot, the route they came by — overriding
    /// whatever route their zone would otherwise imply.</summary>
    public string? RoutePreference { get; init; }

    /// <summary>
    /// True if this character can never be drawn as a killer.
    ///
    /// Two reasons, and only the first is live: carrying no guilt slots (four characters), or
    /// being pinned to a faction (nobody, now). The pinned check stays so that pinning one again
    /// keeps them out of the draw automatically rather than silently making them eligible.
    /// </summary>
    public bool IneligibleAsKiller => Slots.Count == 0 || FixedFaction is not null;
}

/// <summary>
/// The same fact told two ways. Which one a player sees is decided by the deal.
/// </summary>
public sealed record GuiltyInnocent
{
    public string Guilty { get; init; } = "";
    public string Innocent { get; init; } = "";

    /// <summary>Pick the reading this character was dealt.</summary>
    public string For(bool guilty) => guilty ? Guilty : Innocent;
}

/// <summary>
/// A character's physical clue card — the thing printed as a QR and left in a room.
/// </summary>
public sealed record ScriptTrace
{
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";

    /// <summary>Non-null means this trace belongs in one specific zone and never moves, even when
    /// that zone already holds a clue. Portable traces (null) spill to an adjacent clueless zone.</summary>
    public string? AnchorZone { get; init; }

    public bool IsPortable => AnchorZone is null;
}
