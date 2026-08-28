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

    /// <summary>How long the room gets to confer and buzz once Final Jeopardy's buzzers open.</summary>
    public int TimeLimitSeconds { get; set; } = 300;
}

/// <summary>
/// Serialized by name, so these may be reordered or added to without breaking a saved game.
/// </summary>
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
    /// <summary>
    /// The clue is settled and its answer is on the wall. Everything waits here until the host
    /// moves on, because the answer is the part of a quiz people actually want to hear, and
    /// snapping back to the board the instant a team is marked right skips it.
    /// </summary>
    Revealed,
    /// <summary>Board is exhausted. The Final Jeopardy titles and its rules are on screen.</summary>
    FinalIntro,
    /// <summary>The final clue is showing and the buzzers are live.</summary>
    Final,
    /// <summary>The game is over and the winner is up.</summary>
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

    /// <summary>
    /// Who got the clue that is currently revealed, or null if nobody did. Only meaningful in
    /// <see cref="JeopardyPhase.Revealed"/>: once the buzzes are cleared there is otherwise no
    /// record of who won it, and "nobody got it" and "Team Ali got it" are different screens.
    /// </summary>
    public string? RevealedWinnerTeamId { get; set; }

    /// <summary>When the buzzers opened, so a buzz can be reported as a reaction time.</summary>
    public DateTimeOffset? BuzzOpenedAt { get; set; }

    /// <summary>Final's timer ran out with nobody in — buzzers are shut until the host restarts it.</summary>
    public bool FinalTimerExpired { get; set; }

    // Final Jeopardy used to collect a written answer per team and reveal them together, which
    // needed FinalAnswers / FinalCorrectTeamIds / FinalRevealed here. It is now played exactly
    // like any other clue — buzz in, answer aloud, wrong costs you the points — so that state is
    // gone. Teams still write while they confer, but on their own phone and nowhere else, so
    // there is nothing to store. Older saved games simply drop the fields on load.

    /// <summary>
    /// The one code on the wall. Regenerated on reset.
    ///
    /// One, not one per team. A code used to *be* a team — whoever typed Team Ali's four
    /// characters was Team Ali's buzzer, and if a second phone typed them there were two. Now the
    /// code is only a door: it proves you are in the room, and the next question is which name you
    /// are. Your team comes off your roster row after that, which means there is no way to ask for
    /// a team that is not yours, and nothing to guard against.
    /// </summary>
    public string PartyCode { get; set; } = "";

    /// <summary>
    /// Teams whose phone has been through the door, so the board can refuse to start without them.
    ///
    /// Recorded on sign-in rather than inferred from the first buzz, because the whole point is to
    /// know before the game starts — a team that discovers its buzzer does not work on the opening
    /// clue has already lost that clue.
    /// </summary>
    public List<string> JoinedTeamIds { get; set; } = [];

    /// <summary>The host's answer sheet is in somebody's hand. Nobody can judge a clue without it.</summary>
    public bool HostJoined { get; set; }

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
