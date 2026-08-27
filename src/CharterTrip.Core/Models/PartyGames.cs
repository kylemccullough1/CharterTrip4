using System.Text.Json.Serialization;

namespace CharterTrip.Core.Models;

/// <summary>
/// Serialized by name, like <see cref="JeopardyPhase"/>, so the states can be reordered or
/// added to without a saved game coming back as something else.
/// </summary>
public enum PartyGamePhase
{
    NotStarted,
    Playing,
    Finished
}

/// <summary>
/// Police Sketch, Pool Noodle Cups and Beer Run are the same game to a scoreboard: a fixed
/// number of rounds, a point value per unit, and a result to record before moving on. They
/// share this rather than each carrying its own near-identical copy.
///
/// There is no round-by-round history here on purpose. Every award is already a ScoreEntry
/// noted with its round, so the history, the totals and undo all read from the one log
/// instead of a second copy that can disagree with it.
/// </summary>
public sealed class RoundGame
{
    public PartyGamePhase Phase { get; set; } = PartyGamePhase.NotStarted;

    /// <summary>Points per round won (Sketch) or per cup / beer (the other two). Editable before the game starts.</summary>
    public int PointValue { get; set; }

    public int RoundCount { get; set; }

    /// <summary>1-based. Means nothing until Phase is Playing.</summary>
    public int Round { get; set; } = 1;

    /// <summary>Sketch's characters to draw. Empty for the games that have nothing to pick from.</summary>
    public List<string> Prompts { get; set; } = [];

    /// <summary>Prompts already played, so a character cannot come up twice.</summary>
    public List<string> UsedPrompts { get; set; } = [];

    public string? CurrentPrompt { get; set; }
}

/// <summary>
/// The relay is four clocks rather than rounds: every team runs the same legs at once and the
/// fastest one wins. Only the winner scores.
/// </summary>
public sealed class RelayGame
{
    public PartyGamePhase Phase { get; set; } = PartyGamePhase.NotStarted;

    public int WinnerPoints { get; set; } = 100;

    /// <summary>What the winner earns instead when they are short-handed. See <see cref="SmallTeamSize"/>.</summary>
    public int SmallTeamPoints { get; set; } = 120;

    /// <summary>A winning team this size or smaller earns the larger prize for being a person down.</summary>
    public int SmallTeamSize { get; set; } = 5;

    /// <summary>Keyed by TeamId.</summary>
    public Dictionary<string, RelayTimer> Timers { get; set; } = [];
}

/// <summary>
/// Stores the instant the clock started rather than a running count, the same way a buzz does,
/// so every phone watching computes the same elapsed time from the same fact.
/// </summary>
public sealed class RelayTimer
{
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Set when the clock stops. Null while it is running, and while it has never started.</summary>
    public int? ElapsedMs { get; set; }

    [JsonIgnore] public bool Running => StartedAt is not null && ElapsedMs is null;
    [JsonIgnore] public bool Stopped => ElapsedMs is not null;
}

/// <summary>
/// The four games played on their feet — everything that changes while they are being played.
/// The rules and blurbs stay on <see cref="Game"/> with the rest of the games.
/// </summary>
public sealed class PartyGames
{
    public RoundGame Sketch { get; set; } = new();
    public RoundGame NoodleCup { get; set; } = new();
    public RoundGame BeerRun { get; set; } = new();
    public RelayGame Relay { get; set; } = new();
}
