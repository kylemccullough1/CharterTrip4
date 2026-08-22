using System.Text.Json.Serialization;

namespace CharterTrip.Core.Models;

public sealed class JeopardyBoard
{
    public string Title { get; set; } = "";
    public List<JeopardyCategory> Categories { get; set; } = [];
    public JeopardyFinal Final { get; set; } = new();

    /// <summary>Everything that changes while the game is being played. Reset wipes this.</summary>
    public JeopardyGame Game { get; set; } = new();
}

public sealed class JeopardyCategory
{
    public string Name { get; set; } = "";
    public List<JeopardyClue> Clues { get; set; } = [];
}

public sealed class JeopardyClue
{
    public string Id { get; set; } = "";
    public int Value { get; set; }

    /// <summary>What goes up on the board.</summary>
    public string Clue { get; set; } = "";

    /// <summary>What the host is looking for.</summary>
    public string Response { get; set; } = "";

    public string ClueImage { get; set; } = "";
    public string ResponseImage { get; set; } = "";

    /// <summary>A picture-only clue still has something to show.</summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Clue) && string.IsNullOrWhiteSpace(ClueImage);
}

public sealed class JeopardyFinal
{
    public string Category { get; set; } = "Final Jeopardy";
    public int Value { get; set; } = 30;
    public string Clue { get; set; } = "";
    public string Response { get; set; } = "";
}

public enum JeopardyPhase
{
    /// <summary>Title card. Nothing has started.</summary>
    NotStarted,
    /// <summary>Board is up, someone picks a clue.</summary>
    Board,
    /// <summary>A clue is showing and the buzzers are live.</summary>
    Clue,
    /// <summary>Somebody buzzed; the host says right or wrong.</summary>
    Judging,
    /// <summary>Board is exhausted; the final clue is showing.</summary>
    Final,
    /// <summary>Final answers are in and revealed.</summary>
    Finished
}

/// <summary>Live state of a game in progress. Everything here is cleared by a reset.</summary>
public sealed class JeopardyGame
{
    public JeopardyPhase Phase { get; set; } = JeopardyPhase.NotStarted;

    /// <summary>Clues already played, so the board can grey them out.</summary>
    public List<string> UsedClueIds { get; set; } = [];

    public string? CurrentClueId { get; set; }

    /// <summary>Whose turn it is to choose. Picked at random to start, then the last team to answer correctly.</summary>
    public string? PickingTeamId { get; set; }

    /// <summary>Buzzes for the clue on screen, in the order they reached the server.</summary>
    public List<Buzz> Buzzes { get; set; } = [];

    /// <summary>Teams that have already answered this clue wrong and cannot buzz again on it.</summary>
    public List<string> LockedOutTeamIds { get; set; } = [];

    public bool BuzzersOpen { get; set; }

    /// <summary>When the buzzers opened, so a buzz can be reported as a reaction time.</summary>
    public DateTimeOffset? BuzzOpenedAt { get; set; }

    /// <summary>What each team wrote for the final clue.</summary>
    public Dictionary<string, string> FinalAnswers { get; set; } = [];

    /// <summary>Teams the host marked correct on the final.</summary>
    public List<string> FinalCorrectTeamIds { get; set; } = [];

    public bool FinalRevealed { get; set; }

    /// <summary>Short code each team types on their phone. Regenerated on reset.</summary>
    public Dictionary<string, string> BuzzerCodes { get; set; } = [];

    /// <summary>Code for the host's answer sheet, so a stray phone cannot judge the game.</summary>
    public string HostCode { get; set; } = "";

    [JsonIgnore]
    public string? LeadingBuzzTeamId => Buzzes.Count == 0 ? null : Buzzes[0].TeamId;
}

public sealed class Buzz
{
    public string TeamId { get; set; } = "";
    public DateTimeOffset At { get; set; }

    /// <summary>Milliseconds after the buzzers opened. What gets shown on the board.</summary>
    public int Milliseconds { get; set; }
}
