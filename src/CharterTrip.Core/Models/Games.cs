using System.Text.Json.Serialization;

namespace CharterTrip.Core.Models;

public sealed class Game
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public string When { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Blurb { get; set; } = "";
    public List<string> Rules { get; set; } = [];

    /// <summary>Which scoring widget the game page renders. See phase 4.</summary>
    public string Scoring { get; set; } = "generic";

    /// <summary>Relay race legs. Empty for every other game.</summary>
    public List<string> Legs { get; set; } = [];
}

/// <summary>
/// Points are an append-only log, not a running total per team. A team's score is the
/// sum of its entries, which means the leaderboard, the per-game breakdown and undo
/// all derive from one place instead of drifting apart.
/// </summary>
public sealed class ScoreEntry
{
    public string Id { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string GameId { get; set; } = "";
    public int Points { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class Superlative
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Winner { get; set; } = "";
}

public sealed class JeopardyBoard
{
    public List<string> Categories { get; set; } = [];
    public List<int> Values { get; set; } = [];
    public List<JeopardyClue> Clues { get; set; } = [];
}

public sealed class JeopardyClue
{
    public string Category { get; set; } = "";
    public int Value { get; set; }

    /// <summary>What goes on the board — in Jeopardy terms this is the "answer".</summary>
    public string Clue { get; set; } = "";

    /// <summary>The "What is ...?" the team has to say.</summary>
    public string Response { get; set; } = "";

    public string? ImageUrl { get; set; }

    /// <summary>Reconstructed from the meeting notes rather than the finished board — verify before playing.</summary>
    public bool Draft { get; set; }

    public bool Used { get; set; }

    /// <summary>Computed, so it is not persisted.</summary>
    [JsonIgnore]
    public string Key => $"{Category}-{Value}";
}
