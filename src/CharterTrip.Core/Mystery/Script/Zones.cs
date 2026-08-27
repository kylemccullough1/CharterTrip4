namespace CharterTrip.Core.Mystery.Script;

/// <summary>
/// The house, from <c>zones.json</c>.
/// </summary>
public sealed record ScriptZoneBook
{
    public IReadOnlyList<ScriptZone> Zones { get; init; } = [];

    /// <summary>The three ways into the study, keyed by route id: corridor, window, side_path.</summary>
    public IReadOnlyDictionary<string, ScriptAccessRoute> AccessRoutes { get; init; } =
        new Dictionary<string, ScriptAccessRoute>();

    /// <summary>The in-fiction reason the guest wing is closed — which is really the reason the
    /// playable area is eight rooms and not the whole house.</summary>
    public string WestWingFiction { get; init; } = "";

    /// <summary>The zones the generator may place players in — everything except the study.</summary>
    public IEnumerable<ScriptZone> Playable => Zones.Where(z => z.PlayersAllowed);

    /// <summary>The zones that let a killer reach the study, which is what the access slot needs.</summary>
    public IEnumerable<ScriptZone> AccessGranting => Playable.Where(z => z.GrantsAccess);

    public ScriptZone? ById(string id) => Zones.FirstOrDefault(z => z.Id == id);
}

/// <summary>
/// One room. The study is a zone like any other so that it can hold a clue, but it takes no
/// players — it is the murder scene, and standing in it is not a thing guests do.
/// </summary>
public sealed record ScriptZone
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>False for the study alone.</summary>
    public bool PlayersAllowed { get; init; }

    public ScriptCapacity Capacity { get; init; } = new();

    /// <summary>Zones reachable from here. Used for clue spillover and cross-zone sightings.</summary>
    public IReadOnlyList<string> Adjacent { get; init; } = [];

    /// <summary>True if a killer placed here could have reached the study.</summary>
    public bool GrantsAccess { get; init; }

    /// <summary>Where in the room the printed clue card goes. Read by the print sheet, and by
    /// whoever is placing nine QR codes before the guests arrive.</summary>
    public string ClueSpot { get; init; } = "";
}

/// <summary>
/// How many people a room needs to feel occupied, and how many it can hold before it stops being
/// a conversation. The generator treats both as hard constraints.
/// </summary>
public sealed record ScriptCapacity
{
    public int Min { get; init; }
    public int Max { get; init; }
}

/// <summary>
/// One way into the study, and the sentence the reveal uses to describe it.
/// </summary>
public sealed record ScriptAccessRoute
{
    /// <summary>The zones from which this route is available.</summary>
    public IReadOnlyList<string> Zones { get; init; } = [];

    /// <summary>Reveal-text fragment: "through the corridor, past everyone, in plain sight".</summary>
    public string Label { get; init; } = "";
}
