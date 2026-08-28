using System.Text.Json.Serialization;

namespace CharterTrip.Core.Models;

/// <summary>
/// The spelling bee.
///
/// It is played by <em>people</em> rather than by teams: the field is a shuffled row of everyone
/// who joined, elimination is per person, and a team's score is simply what its members spelled
/// correctly along the way.
///
/// Three screens, and which one a device gets is the whole design. The wall is pointed at the
/// room and never shows the word — in a bee the word <em>is</em> the answer, so anything the room
/// can see, the speller can see. The word lives on the host's phone and nowhere else. Everyone
/// else's phone is how they got into the game in the first place.
/// </summary>
public sealed class SpellingBee
{
    public string Title { get; set; } = "Spelling Bee";

    /// <summary>
    /// Every word this bee has drawn, oldest first, and the last one is the word in play.
    ///
    /// Not a deck. Words are drawn one at a time out of the embedded Scripps bank as turns come
    /// up, because the difficulty they are drawn at is the host's to move while the bee is
    /// running — a hand dealt up front would fix that decision before anybody had spelled
    /// anything. Keeping the drawn ones here is what stops a word coming round twice, skipped
    /// words included.
    /// </summary>
    public List<BeeWord> Words { get; set; } = [];

    /// <summary>
    /// Which tier the first word comes out of. Set before the bee starts and copied into the
    /// game at Start, after which it is <see cref="BeeGame.DifficultyKey"/> that matters — the
    /// host moves that one up and down as the room copes or does not.
    /// </summary>
    public string DifficultyKey { get; set; } = "moderate";

    /// <summary>What a correct word is worth to the speller's team, every single time.</summary>
    public int PointsPerWord { get; set; } = 5;

    /// <summary>Everything that changes while the bee is being played. Reset wipes this.</summary>
    public BeeGame Game { get; set; } = new();
}

public sealed class BeeWord
{
    public string Id { get; set; } = "";

    /// <summary>The word itself. For the host's phone — the wall only sees it once the turn is over.</summary>
    public string Word { get; set; } = "";

    /// <summary>Which tier it was drawn from, so the host's phone can say how hard it is meant to be.</summary>
    public string TierKey { get; set; } = "";

    /// <summary>
    /// What a speller is allowed to ask for, and therefore what the host has to be able to answer
    /// without leaving the microphone. Filled from the word bank at deal time.
    ///
    /// Any of these may be blank — the bank is compiled from public dictionary data and coverage
    /// is not total — so the host's phone shows only the lines it actually has rather than a row
    /// of empty labels implying the host forgot something.
    /// </summary>
    public string Definition { get; set; } = "";

    public string PartOfSpeech { get; set; } = "";

    /// <summary>The word used in a sentence. The one a speller asks for most and the sparsest.</summary>
    public string Sentence { get; set; } = "";

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Word);

    /// <summary>Whether there is anything to tell a speller who asks.</summary>
    [JsonIgnore]
    public bool HasHelp =>
        !string.IsNullOrWhiteSpace(Definition) ||
        !string.IsNullOrWhiteSpace(PartOfSpeech) ||
        !string.IsNullOrWhiteSpace(Sentence);
}

/// <summary>
/// Serialized by name, so these may be reordered or added to without breaking a saved game.
/// </summary>
public enum BeePhase
{
    /// <summary>Title card, join codes, and the faces of everyone who has joined.</summary>
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
    /// The row across the top of the wall, and the turn order, and the same thing twice on
    /// purpose. Shuffled once when the bee starts and then never touched again: a row that
    /// re-sorted itself as people went out would move a face out from under the eye following it,
    /// and the whole point of the row is that you can see your turn coming.
    /// </summary>
    public List<string> Order { get; set; } = [];

    /// <summary>Who is out, in the order they went out. Their place in <see cref="Order"/> keeps.</summary>
    public List<string> Eliminated { get; set; } = [];

    /// <summary>Who is at the microphone. Also who the <see cref="BeePhase.Revealed"/> card is about.</summary>
    public string? CurrentPersonId { get; set; }

    /// <summary>
    /// Which tier the next word comes out of, moved up and down by the host while the bee runs.
    ///
    /// Live rather than fixed because a bee is judged by feel: three eliminations in a row means
    /// it is too hard, and a round where nobody so much as hesitates means it is too easy. It
    /// takes effect on the next word drawn and never rewrites the one somebody is already
    /// standing there spelling.
    /// </summary>
    public string DifficultyKey { get; set; } = "moderate";

    /// <summary>
    /// How <see cref="CurrentPersonId"/> did, for the card on screen. "Ben got it" and "Ben is
    /// out" are different screens, and once the turn is over nothing else records which it was.
    /// </summary>
    public bool LastCorrect { get; set; }

    /// <summary>
    /// Who the wall should be knocking over right now, or null. It cannot be derived from
    /// <see cref="Eliminated"/> — that keeps everyone who ever went out, and the animation is
    /// about exactly one of them. Cleared when the host moves on.
    /// </summary>
    public string? JustEliminatedPersonId { get; set; }

    /// <summary>
    /// Who came back in on the revival now on screen, if this turn triggered one. Cleared when
    /// the host moves on. It cannot be derived after the fact — the revived are ordinary
    /// survivors the moment they are back in the field.
    ///
    /// This and <see cref="JustEliminatedPersonId"/> are mutually exclusive: a miss either puts
    /// somebody out or refills the field, never both. Which of the two is set is what the wall
    /// keys its whole revealed screen off, because "missed and out" and "missed and everybody
    /// comes back" are opposite things to look at.
    /// </summary>
    public List<string> JustRevived { get; set; } = [];

    /// <summary>
    /// Everyone who has scanned the guest code and tapped their name. Only these people are dealt
    /// into the running order, so a phone left in a pocket does not become a turn the room waits
    /// on.
    /// </summary>
    public List<string> Ready { get; set; } = [];

    /// <summary>
    /// Which rule the wall is showing while the host talks the room through them, or -1 for none.
    ///
    /// On the shared game rather than on the wall's own state because it is the host's phone that
    /// drives it, and the wall is a screen with nobody standing at it. One rule at a time: a
    /// six-item list on a wall is read by everybody at their own pace and listened to by nobody.
    /// </summary>
    public int RuleSlide { get; set; } = -1;

    /// <summary>
    /// The one code every guest uses. Unlike Jeopardy there is nothing per-person to protect —
    /// a guest picks their own name off a list once they are in — so one code on the wall is one
    /// thing to scan rather than twenty-five.
    /// </summary>
    public string GuestCode { get; set; } = "";
}
