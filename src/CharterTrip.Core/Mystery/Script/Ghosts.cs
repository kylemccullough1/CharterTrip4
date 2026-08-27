namespace CharterTrip.Core.Mystery.Script;

/// <summary>
/// The dead and the staff, from <c>ghosts_npcs.json</c>.
/// </summary>
public sealed record ScriptGhostBook
{
    public ScriptGhosts Ghosts { get; init; } = new();
    public ScriptNpcs Npcs { get; init; } = new();
}

/// <summary>
/// What a convicted player gets instead of going home.
///
/// Canned messages and emoji only. That is not a limitation to work around later — it is the rule
/// that lets convicted players stay in the room at all, because a dead killer who can type is a
/// dead game.
/// </summary>
public sealed record ScriptGhosts
{
    public string Rules { get; init; } = "";

    /// <summary>The twelve things a ghost is allowed to say. Some carry a {name} placeholder.</summary>
    public IReadOnlyList<string> CannedMessages { get; init; } = [];

    public ScriptHaunt Haunt { get; init; } = new();

    /// <summary>Emoji that float up the main screen anonymously during trials.</summary>
    public IReadOnlyList<string> TrialReactions { get; init; } = [];
}

/// <summary>
/// A thunderclap and a screen flicker on one living player's phone. Zero information, and the
/// scream it produces is the entire payout.
/// </summary>
public sealed record ScriptHaunt
{
    public int ChargesPerGhost { get; init; }
    public string Effect { get; init; } = "";
    public string Note { get; init; } = "";
}

/// <summary>
/// The four organizer seats: the host and three facilitators.
/// </summary>
/// <summary>
/// One of the four parts the organizers play, in a single shape the pickers can render.
///
/// Braun and the three facilitators are different things in the content — he is the victim and
/// then the game master, they are guests who are secretly staff — but from the outside they are
/// the same question: which of these four are you tonight?
/// </summary>
/// <param name="Id">Stable, and derived from the first name — see <see cref="ScriptNpcs.Roles"/>.</param>
/// <param name="IsHost">True for Braun alone. He runs the evening; they work the room.</param>
public sealed record ScriptNpcRole(
    string Id,
    string Name,
    string Title,
    string Zone,
    IReadOnlyList<string> Orders,
    bool IsHost);

public sealed record ScriptNpcs
{
    public ScriptBraun Braun { get; init; } = new();
    public IReadOnlyList<ScriptFacilitator> Facilitators { get; init; } = [];

    /// <summary>
    /// The four organizer parts, Braun first.
    ///
    /// Ids come from the first name rather than the list position, so reordering the facilitators
    /// in the content cannot silently reassign who somebody claimed to be.
    /// </summary>
    public IReadOnlyList<ScriptNpcRole> Roles =>
    [
        new("braun", Braun.Name, "Host of the evening", "the study, then the back room",
            [Braun.AlivePhase, Braun.DeadPhase, Braun.HardRule], IsHost: true),

        .. Facilitators.Select(f =>
            new ScriptNpcRole(FirstName(f.Name), f.Name, f.Title, f.Zone, f.Orders, IsHost: false))
    ];

    public ScriptNpcRole? RoleById(string id) =>
        Roles.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string FirstName(string name) =>
        (name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name).ToLowerInvariant();
}

/// <summary>
/// The victim, and then the game master.
/// </summary>
public sealed record ScriptBraun
{
    public string Name { get; init; } = "";
    public string PlayedBy { get; init; } = "";

    /// <summary>Round 1: works the room, explains the phones in-fiction as party novelty.</summary>
    public string AlivePhase { get; init; } = "";

    /// <summary>After the scream: the game-master console.</summary>
    public string DeadPhase { get; init; } = "";

    /// <summary>Braun's player must not be seen after the scream until the reveal. The empty chair
    /// is doing story work.</summary>
    public string HardRule { get; init; } = "";
}

/// <summary>
/// A guest who is secretly staff — obviously not a suspect, and working the room on purpose.
/// Their console shows the guilty list, which is how a shy-player game still works.
/// </summary>
public sealed record ScriptFacilitator
{
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";

    /// <summary>Where they are stationed, or "roaming".</summary>
    public string Zone { get; init; } = "";

    public string PlayedBy { get; init; } = "";
    public IReadOnlyList<string> Orders { get; init; } = [];
}
