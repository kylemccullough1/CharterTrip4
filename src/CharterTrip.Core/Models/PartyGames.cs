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
/// Someone for a team to draw, and a picture of them for whoever is describing it.
///
/// The picture is a link rather than a copy: these are all somebody else's characters, and the
/// host wants to glance at one for thirty seconds, not own it. Blank is fine — the game page
/// offers to look one up, and whatever is pasted back is kept for next time.
/// </summary>
public sealed class SketchCharacter
{
    public string Name { get; set; } = "";
    public string ImageUrl { get; set; } = "";
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

    /// <summary>Sketch's cast. Empty for the games that have nothing to pick from.</summary>
    public List<SketchCharacter> Characters { get; set; } = [];

    /// <summary>Characters already drawn, by name, so nobody comes up twice.</summary>
    public List<string> UsedCharacters { get; set; } = [];

    public string? CurrentCharacter { get; set; }

    /// <summary>
    /// Who is still in it, when the last round ended level. Empty for an ordinary round.
    ///
    /// A weekend-long scoreboard cannot end a game "joint second", so a tie at the top sends
    /// exactly the teams who tied into a sudden-death round and keeps doing it until somebody
    /// is actually ahead. See <c>RoundGameService.NextRound</c>.
    /// </summary>
    public List<string> TieBreakTeamIds { get; set; } = [];

    [JsonIgnore] public bool IsSuddenDeath => TieBreakTeamIds.Count > 0;
}

/// <summary>
/// The relay is four clocks rather than rounds: one gun starts every team at once, each lead
/// stops their own, and the fastest wins. Only the winner scores.
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

    /// <summary>Who is in the run-off, when two clocks came back identical. Empty for an ordinary race.</summary>
    public List<string> TieBreakTeamIds { get; set; } = [];

    [JsonIgnore] public bool IsRunOff => TieBreakTeamIds.Count > 0;
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

    /// <summary>Waiting on the gun.</summary>
    [JsonIgnore] public bool Armed => StartedAt is null;
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
