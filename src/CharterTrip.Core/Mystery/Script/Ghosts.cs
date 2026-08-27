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
public sealed record ScriptNpcs
{
    public ScriptBraun Braun { get; init; } = new();
    public IReadOnlyList<ScriptFacilitator> Facilitators { get; init; } = [];
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
