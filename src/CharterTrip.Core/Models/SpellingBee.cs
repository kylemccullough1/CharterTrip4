using System.Text.Json.Serialization;

namespace CharterTrip.Core.Models;

/// <summary>
/// The spelling bee.
///
/// Unlike Jeopardy, which is played by teams buzzing against each other, the bee is played
/// by <em>people</em>: the roster is the field, elimination is per person, and a team only
/// wins when one of its members is the last one standing. There are no phones either — one
/// person spells aloud and the host says right or wrong — so none of Jeopardy's join-code
/// machinery has an equivalent here.
/// </summary>
public sealed class SpellingBee
{
    public string Title { get; set; } = "Spelling Bee";

    /// <summary>The word list, in the order the host reads it. Easiest first — the order is the difficulty curve.</summary>
    public List<BeeWord> Words { get; set; } = [];

    /// <summary>What the winning speller's team takes. One award, at the end.</summary>
    public int WinnerPoints { get; set; } = 10;

    /// <summary>Everything that changes while the bee is being played. Reset wipes this.</summary>
    public BeeGame Game { get; set; } = new();
}

public sealed class BeeWord
{
    public string Id { get; set; } = "";

    /// <summary>The word itself. For the host's eyes — it only goes on the wall once the turn is over.</summary>
    public string Word { get; set; } = "";

    /// <summary>Definition, origin, or a sentence — whatever the host reads when the speller asks.</summary>
    public string Hint { get; set; } = "";

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Word);
}

/// <summary>
/// Serialized by name, so these may be reordered or added to without breaking a saved game.
/// </summary>
public enum BeePhase
{
    /// <summary>Title card. Nobody is at the microphone.</summary>
    NotStarted,

    /// <summary>Someone has a word and the host is waiting to hear it spelled.</summary>
    Spelling,

    /// <summary>
    /// The turn is settled and the word is on the wall, spelled correctly, so the room gets to
    /// see what it actually was. Everything waits here until the host moves on: going straight
    /// to the next speller skips the only moment the spelling is ever visible.
    /// </summary>
    Revealed,

    /// <summary>One speller left and they earned it.</summary>
    Finished
}

/// <summary>Live state of a bee in progress. Everything here is cleared by a reset.</summary>
public sealed class BeeGame
{
    public BeePhase Phase { get; set; } = BeePhase.NotStarted;

    /// <summary>
    /// Everyone still in. This is a queue: a speller who survives their turn goes to the back,
    /// so "the next person up for this team" is simply the first survivor belonging to it, and
    /// no per-team bookmark has to be kept in step with this list.
    /// </summary>
    public List<string> Survivors { get; set; } = [];

    /// <summary>
    /// Everyone out, oldest elimination first. The order is load-bearing: the revival rule
    /// reaches for the <em>most recently</em> eliminated member of a team, which is the tail.
    /// </summary>
    public List<string> Eliminated { get; set; } = [];

    /// <summary>
    /// Index into <c>trip.Teams</c> of the team that spelled last, or -1 before the first turn.
    ///
    /// This is what makes turns rotate by team rather than by person, and it is not redundant
    /// with the <see cref="Survivors"/> queue however much it looks it. Rotating that queue
    /// alone gives, for teams A[Ann, Ben, Cal] and B[Dee]: Ann, Dee, Ben, Cal — where the rules
    /// call for Dee, because every other turn is hers no matter how few of her team are left.
    /// </summary>
    public int TeamCursor { get; set; } = -1;

    /// <summary>Who is at the microphone. Also who the <see cref="BeePhase.Revealed"/> card is about.</summary>
    public string? CurrentPersonId { get; set; }

    /// <summary>How far down <see cref="SpellingBee.Words"/> the host has read. Words are never reused.</summary>
    public int WordCursor { get; set; }

    /// <summary>
    /// How <see cref="CurrentPersonId"/> did, for the card on screen. "Ben got it" and "Ben is
    /// out" are different screens, and once the turn is over nothing else records which it was.
    /// </summary>
    public bool LastCorrect { get; set; }

    /// <summary>
    /// Who came back in the revival now on screen, if this turn triggered one. Cleared when the
    /// host moves on. It cannot be derived after the fact — the revived are ordinary survivors
    /// the moment they are back in the list.
    /// </summary>
    public List<string> JustRevived { get; set; } = [];
}
