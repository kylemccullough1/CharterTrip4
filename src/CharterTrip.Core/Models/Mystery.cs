namespace CharterTrip.Core.Models;

/// <summary>
/// Murder at Braun Manor, as it exists in trip.json.
///
/// Two halves, and the split is the whole design. <see cref="Story"/> is what somebody wrote — the
/// characters, the rooms, the clues, the prose — and it is edited on the site like everything else
/// on this trip. <see cref="Play"/> is what the room has done to it on the night, and discarding a
/// game clears that half and leaves the story alone, so the evening can be rehearsed as many times
/// as it takes against the story being written.
///
/// This used to be a generator: a seeded dealer placed everybody, drew three killers, picked red
/// herrings and laid out clues, and a compiler composed every sentence from templates and a
/// guilty-or-innocent reading per character. That machinery bought replayability, which is worth
/// nothing to a game played once. It is gone, and with it the reason the prose had to be embedded
/// and read-only.
/// </summary>
public sealed class MysteryState
{
    public MysteryPhase Phase { get; set; } = MysteryPhase.Lobby;

    /// <summary>What was written. Edited at /games/mystery/story, survives a discard.</summary>
    public MysteryStory Story { get; set; } = new();

    /// <summary>What the room did. Cleared by a discard.</summary>
    public MysteryPlay Play { get; set; } = new();
}

// =============================================================================================
//  The phases
// =============================================================================================

/// <summary>
/// The evening, in order.
///
/// Serialized by name so the enum can be reordered without breaking a saved game — and, more to the
/// point, never compared with &lt; or &gt;. <see cref="MysteryPhases.Order"/> is the only authority on
/// sequence, because a gate that silently moves when somebody tidies this list is the one bug in
/// this build with no recovery: killers' briefings appearing during the party ends the night.
/// </summary>
public enum MysteryPhase
{
    /// <summary>No game yet. Two QR doors and the story editor.</summary>
    Lobby,

    /// <summary>People arriving, claiming parts, uploading photos.</summary>
    Assembling,

    /// <summary>The screen turns into Braun Manor.</summary>
    Welcome,

    /// <summary>Braun walks the room through the deck.</summary>
    Presentation,

    /// <summary>
    /// Everybody stands up in turn and says who they are, while the wall shows their face. No
    /// roles exist yet. Replaced the room-by-room mingle: nobody is sent anywhere.
    /// </summary>
    Introductions,

    /// <summary>Lights, thunder, the scream.</summary>
    Murder,

    /// <summary>The study. Roles drop here.</summary>
    StudyScene,

    /// <summary>Thirty minutes: mingle, scan each other, find the cards.</summary>
    Investigation,

    /// <summary>The accusation round — five minutes of who seemed strange — before the first trial.</summary>
    Discussion1,
    Trial1,

    /// <summary>Deliberation between the trials, where the heavy abilities come online.</summary>
    Discussion2,
    Trial2,

    /// <summary>The final deliberation. No new powers.</summary>
    Discussion3,
    Trial3,
    Reveal
}

/// <summary>
/// Where a phase sits in the evening, and every gate that depends on it.
///
/// Nothing anywhere else may compare two <see cref="MysteryPhase"/> values directly. The enum's
/// declaration order and the game's order agree today and there is no mechanism that keeps them
/// agreeing, so <see cref="Order"/> is stated once, tested against the enum, and read by everything.
/// </summary>
public static class MysteryPhases
{
    /// <summary>The evening, in the order it happens.</summary>
    public static readonly IReadOnlyList<MysteryPhase> Order =
    [
        MysteryPhase.Lobby,
        MysteryPhase.Assembling,
        MysteryPhase.Welcome,
        MysteryPhase.Presentation,
        MysteryPhase.Introductions,
        MysteryPhase.Murder,
        MysteryPhase.StudyScene,
        MysteryPhase.Investigation,
        MysteryPhase.Discussion1,
        MysteryPhase.Trial1,
        MysteryPhase.Discussion2,
        MysteryPhase.Trial2,
        MysteryPhase.Discussion3,
        MysteryPhase.Trial3,
        MysteryPhase.Reveal
    ];

    public static int IndexOf(MysteryPhase phase)
    {
        for (var i = 0; i < Order.Count; i++)
            if (Order[i] == phase) return i;

        // Unreachable while the reflection test passes, and a loud failure rather than a silent
        // "somewhere near the start" if it ever does not.
        throw new ArgumentOutOfRangeException(nameof(phase), phase, "Phase is missing from MysteryPhases.Order.");
    }

    public static bool AtOrAfter(MysteryPhase current, MysteryPhase gate) =>
        IndexOf(current) >= IndexOf(gate);

    public static MysteryPhase? Next(MysteryPhase phase)
    {
        var i = IndexOf(phase);
        return i + 1 < Order.Count ? Order[i + 1] : null;
    }

    /// <summary>The trials, for code that wants to know whether to open a ballot.</summary>
    public static bool IsTrial(MysteryPhase phase) =>
        phase is MysteryPhase.Trial1 or MysteryPhase.Trial2 or MysteryPhase.Trial3;

    /// <summary>
    /// The phase in words, for anywhere a person reads it.
    ///
    /// <c>phase.ToString()</c> was going straight onto the console and the board, which is where
    /// "FinalVote" and "Trial3" came from. Enum names are identifiers; this is the English.
    /// </summary>
    public static string Label(MysteryPhase phase) => phase switch
    {
        MysteryPhase.Lobby => "Before the evening",
        MysteryPhase.Assembling => "Arriving",
        MysteryPhase.Welcome => "Welcome",
        MysteryPhase.Presentation => "The briefing",
        MysteryPhase.Introductions => "Introductions",
        MysteryPhase.Murder => "The murder",
        MysteryPhase.StudyScene => "The study",
        MysteryPhase.Investigation => "Investigation",
        MysteryPhase.Discussion1 => "Accusation round",
        MysteryPhase.Trial1 => "First trial",
        MysteryPhase.Discussion2 => "Deliberation",
        MysteryPhase.Trial2 => "Second trial",
        MysteryPhase.Discussion3 => "Final deliberation",
        MysteryPhase.Trial3 => "Final trial",
        MysteryPhase.Reveal => "The whole truth",
        _ => Spaced(phase.ToString())
    };

    /// <summary>Two or three words, for the jump strip where fourteen of these sit side by side.</summary>
    public static string ShortLabel(MysteryPhase phase) => phase switch
    {
        MysteryPhase.Lobby => "Lobby",
        MysteryPhase.Assembling => "Arriving",
        MysteryPhase.Presentation => "Briefing",
        MysteryPhase.Introductions => "Intros",
        MysteryPhase.StudyScene => "Study",
        MysteryPhase.Discussion1 => "Accuse",
        MysteryPhase.Trial1 => "Trial 1",
        MysteryPhase.Discussion2 => "Talk 2",
        MysteryPhase.Trial2 => "Trial 2",
        MysteryPhase.Discussion3 => "Final talk",
        MysteryPhase.Trial3 => "Trial 3",
        MysteryPhase.Reveal => "Reveal",
        _ => Label(phase)
    };

    /// <summary>
    /// A last resort for a phase nobody wrote a label for: "FinalVote" becomes "Final vote".
    ///
    /// Here rather than at each call site so a phase added later reads as English on every surface
    /// from the moment it exists, instead of shipping as a run-together identifier.
    /// </summary>
    public static string Spaced(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase)) return "";

        var text = new System.Text.StringBuilder(pascalCase.Length + 4);
        text.Append(pascalCase[0]);

        for (var i = 1; i < pascalCase.Length; i++)
        {
            var c = pascalCase[i];

            // "Trial3" wants a break before the digit too, or the strip reads "Trial3".
            var boundary = char.IsUpper(c) || (char.IsDigit(c) && !char.IsDigit(pascalCase[i - 1]));
            if (boundary) text.Append(' ');

            text.Append(boundary && char.IsUpper(c) ? char.ToLowerInvariant(c) : c);
        }

        return text.ToString();
    }

    /// <summary>
    /// Roles, factions and abilities land when the room reaches the study — not a moment sooner.
    /// </summary>
    public static bool RolesRevealed(MysteryPhase phase) =>
        AtOrAfter(phase, MysteryPhase.StudyScene);

    /// <summary>
    /// Who scanned which clue, and when.
    ///
    /// Recorded from the first scan and shown to nobody until the deliberation after the first
    /// trial, where it becomes the detectives' tool for working out which clues were tampered
    /// with. Withholding it is the mechanic: a public movement log during the investigation and
    /// the accusation round would make every alibi checkable and there would be nothing left to
    /// lie about.
    /// </summary>
    public static bool TrailVisible(MysteryPhase phase) =>
        AtOrAfter(phase, MysteryPhase.Discussion2);
}

/// <summary>
/// How long each timed phase is meant to take.
///
/// A suggestion, never a gate: the host's console counts it down and the host moves the evening
/// on when the room is ready, which may be before or after. Null for the phases that have no
/// natural length — a trial takes as long as the vote takes, a briefing as long as Braun talks.
/// </summary>
public static class MysteryPhaseDurations
{
    public static TimeSpan? For(MysteryPhase phase) => phase switch
    {
        MysteryPhase.Investigation => TimeSpan.FromMinutes(30),
        MysteryPhase.Discussion1 => TimeSpan.FromMinutes(5),
        MysteryPhase.Discussion2 => TimeSpan.FromMinutes(10),
        MysteryPhase.Discussion3 => TimeSpan.FromMinutes(10),
        _ => null
    };
}

// =============================================================================================
//  The story — everything somebody wrote
// =============================================================================================

/// <summary>
/// The written game. Seeded once from <c>data/braun-manor/</c>, then edited on the site.
///
/// This lives in trip.json rather than embedded in the assembly, which reverses the old design. The
/// old rule existed because a compiler template-filled the prose and because rewriting 110 KB on
/// every vote is wasteful. There is no compiler now, and the story has to be editable from a phone
/// in a kitchen on the afternoon of the party, so it is ordinary state — exactly like the Jeopardy
/// board's clues.
/// </summary>
public sealed class MysteryStory
{
    public string Title { get; set; } = "Murder at the Braun Manor";

    /// <summary>False until the content has been copied in from the embedded seed.</summary>
    public bool Seeded { get; set; }

    /// <summary>Twenty-one guests plus the four house parts.</summary>
    public List<MysteryCharacter> Characters { get; set; } = [];

    public List<MysteryZone> Zones { get; set; } = [];
    public List<MysteryFaction> Factions { get; set; } = [];

    /// <summary>Nine cards, each pinned to a room. Fixed — there is no layout step any more.</summary>
    public List<MysteryClueCard> Clues { get; set; } = [];

    /// <summary>The deck Braun walks the room through before the party starts.</summary>
    public List<MysterySlide> Slides { get; set; } = [];

    /// <summary>What the phase machine and the staff can send to a phone.</summary>
    public List<MysteryObjectiveTemplate> Objectives { get; set; } = [];

    /// <summary>Pre-existing friction between two characters. Nothing to do with the murder.</summary>
    public List<MysteryBeef> Beefs { get; set; } = [];

    public MysteryBeats Beats { get; set; } = new();

    public MysteryCharacter? Character(string id) =>
        Characters.FirstOrDefault(c => c.Id == id);

    public MysteryZone? Zone(string id) => Zones.FirstOrDefault(z => z.Id == id);
    public MysteryFaction? Faction(string id) => Factions.FirstOrDefault(f => f.Id == id);
    public MysteryClueCard? Clue(string id) => Clues.FirstOrDefault(c => c.Id == id);

    /// <summary>The twenty-one parts a guest can be dealt.</summary>
    public IEnumerable<MysteryCharacter> Guests => Characters.Where(c => c.Staff is null);

    /// <summary>Braun and the three facilitators.</summary>
    public IEnumerable<MysteryCharacter> StaffParts => Characters.Where(c => c.Staff is not null);

    public IEnumerable<MysteryCharacter> Killers => Characters.Where(c => c.IsKiller);
}

/// <summary>
/// One character, fixed.
///
/// Everything here used to have two readings — a guilty one and an innocent one — because the
/// generator decided on the night which you were showing. There is one story now, so there is one
/// reading, and whether somebody looks guilty is something that was written rather than drawn.
/// </summary>
public sealed class MysteryCharacter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    // --- the sheet, tab one -------------------------------------------------------------------

    public int Age { get; set; }
    public string Sex { get; set; } = "";

    /// <summary>What they do. "Politician", "Family Doctor", "Chauffeur".</summary>
    public string Job { get; set; } = "";

    /// <summary>How they talk, for the player. Not shown to anybody else.</summary>
    public string Voice { get; set; } = "";

    public string Backstory { get; set; } = "";
    public string WhyInvited { get; set; } = "";

    /// <summary>
    /// Always present. Nobody at this party likes James Braun, and the sheet says why — it is the
    /// hook that makes an innocent person evasive, which is what makes the room hard to read.
    /// </summary>
    public string DislikesBraun { get; set; } = "";

    // --- the truth ----------------------------------------------------------------------------

    /// <summary>Where they spent the evening. Fixed; drives who saw them.</summary>
    public string ZoneId { get; set; } = "";

    public string FactionId { get; set; } = "";

    /// <summary>access, means or signature for the three killers. Null for everybody else.</summary>
    public string? GuiltSlot { get; set; }

    /// <summary>
    /// Written to read as guilty, and isn't. A content decision now rather than a draw — used by
    /// the reveal, and by nothing during play, because the whole point is that it is invisible.
    /// </summary>
    public bool IsHerring { get; set; }

    /// <summary>What they were doing, in their own account.</summary>
    public string Observable { get; set; } = "";

    /// <summary>How somebody else in the room would describe it.</summary>
    public string SeenAs { get; set; } = "";

    /// <summary>The other claimant, for the two inheritance players.</summary>
    public string? RivalCharacterId { get; set; }

    // --- their material -----------------------------------------------------------------------

    public MysteryDialogue Dialogue { get; set; } = new();

    /// <summary>
    /// The three things anybody who scans this character's badge may ask them, and what they
    /// answer. One is where they were when Braun died, one is something else worth knowing, and
    /// one is worth nothing — and the asker is not told which. Empty for staff.
    /// </summary>
    public List<MysteryQuestion> Questions { get; set; } = [];

    public string SignatureItem { get; set; } = "";

    /// <summary>What gets worked into a clue when somebody frames them, or frames themselves.</summary>
    public string TamperInsert { get; set; } = "";

    /// <summary>
    /// Who they really were and why they did what they did, read out at the end if they won.
    /// Written for everybody with a role; the guests of the house share one line in
    /// <see cref="MysteryBeats.VillagerEpilogue"/> instead, because nine variations on "had a
    /// nice evening" is not an ending anybody watches.
    /// </summary>
    public string Epilogue { get; set; } = "";

    /// <summary>
    /// Braun and the three facilitators.
    ///
    /// Real characters with their own sheets and their own things to say — they are in the room all
    /// night and people will talk to them — but no faction, no clue material and no vote. What they
    /// have instead is the Control tab.
    /// </summary>
    public MysteryStaffRole? Staff { get; set; }

    public bool IsKiller => GuiltSlot is not null;
    public bool IsStaff => Staff is not null;
}

public enum MysteryStaffRole
{
    /// <summary>James Braun. Runs the evening, dies in the middle of it.</summary>
    Host,

    /// <summary>Leo, Chloe, Bertram. Keep the room moving.</summary>
    Facilitator
}

/// <summary>
/// What a character has to say before anybody has died.
///
/// The mingling round's entire job is getting twenty-five people talking to each other in character,
/// and the thing that stops that happening is somebody holding a phone with nothing on it. These are
/// the lines that get handed to them.
/// </summary>
public sealed class MysteryDialogue
{
    /// <summary>About themselves.</summary>
    public List<string> Life { get; set; } = [];

    /// <summary>The safest subject in any room, and the easiest opener for a stuck player.</summary>
    public List<string> Weather { get; set; } = [];

    public List<MysteryTopic> Topics { get; set; } = [];
}

public sealed class MysteryTopic
{
    public string Prompt { get; set; } = "";
    public List<string> Lines { get; set; } = [];
}

public sealed class MysteryZone
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>The study takes no players. It is where the body isn't.</summary>
    public bool PlayersAllowed { get; set; } = true;

    public string Notes { get; set; } = "";
    public List<string> Adjacent { get; set; } = [];

    /// <summary>Where the clue card is hidden, for whoever places them.</summary>
    public string ClueSpot { get; set; } = "";
}

public sealed class MysteryFaction
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>What this faction is, in the player's own words.</summary>
    public string Blurb { get; set; } = "";

    /// <summary>What they know that nobody else does.</summary>
    public string Knowledge { get; set; } = "";

    public string WinCondition { get; set; } = "";
    public List<MysteryAbility> Abilities { get; set; } = [];

    /// <summary>Do these players know each other? Killers do; jesters emphatically do not.</summary>
    public bool KnowsEachOther { get; set; }
}

/// <summary>
/// What a question is worth, so the story can be checked for exactly one of each and the
/// asker can never be told.
/// </summary>
public enum MysteryQuestionImportance
{
    /// <summary>Where were you when Braun was murdered — worded for the character.</summary>
    Alibi,

    /// <summary>Something else that matters tonight.</summary>
    Important,

    /// <summary>Colour. Plausible enough to ask, worth nothing.</summary>
    Useless
}

/// <summary>
/// One question somebody can ask a character, and what they say back.
///
/// <see cref="CoverAnswer"/> is the version a killer (or a jester) gives once their story has a
/// lie in it: it replaces <see cref="Answer"/> the moment their tamper fires, with <c>{target}</c>
/// filled by whoever they pointed at. Only the two answers that matter get one; the useless one
/// never changes.
/// </summary>
public sealed class MysteryQuestion
{
    public string Id { get; set; } = "";
    public MysteryQuestionImportance Importance { get; set; } = MysteryQuestionImportance.Important;
    public string Prompt { get; set; } = "";
    public string Answer { get; set; } = "";
    public string? CoverAnswer { get; set; }

    public bool HasCover => !MysteryText.IsPlaceholder(CoverAnswer);
}

public sealed class MysteryAbility
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>
    /// What the phone asks for before it fires: <c>character</c>, <c>clue</c> (a card the player
    /// has scanned) or <c>none</c>. Content, so the picker follows the story rather than a list
    /// of ability ids in the page.
    /// </summary>
    public string Target { get; set; } = "character";

    public int Charges { get; set; } = 1;

    /// <summary>One charge across the whole faction, rather than one each.</summary>
    public bool Shared { get; set; }

    /// <summary>The phase this comes online in.</summary>
    public MysteryPhase Unlock { get; set; } = MysteryPhase.StudyScene;

    /// <summary>Two-mode abilities: plant/scrub, shield/decoy, subtle/blatant.</summary>
    public List<MysteryAbilityMode> Modes { get; set; } = [];

    public bool HasModes => Modes.Count > 0;
}

public sealed class MysteryAbilityMode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>
/// One of the nine printed cards, pinned to a room.
/// </summary>
public sealed class MysteryClueCard
{
    public string Id { get; set; } = "";
    public string ZoneId { get; set; } = "";

    /// <summary>"the handkerchief", "the cut stem".</summary>
    public string Name { get; set; } = "";

    /// <summary>What somebody reads when they scan it.</summary>
    public string Text { get; set; } = "";

    /// <summary>Whose it is. Null for a card that belongs to the scene rather than a person.</summary>
    public string? AboutCharacterId { get; set; }
}

public sealed class MysterySlide
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>What the screen says.</summary>
    public List<string> Bullets { get; set; } = [];

    /// <summary>What Braun reads out loud before tapping Next.</summary>
    public string BraunSays { get; set; } = "";

    /// <summary>Which diagram to draw beside the text, if any. "phone-tabs", "factions", "clue".</summary>
    public string? Figure { get; set; }

    /// <summary>Arrows onto the figure. Coordinates are fractions of its box.</summary>
    public List<MysterySlideCallout> Callouts { get; set; } = [];
}

public sealed class MysterySlideCallout
{
    public string Text { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>
/// Something the game can ask somebody to do.
///
/// A template, not an instance — issuing one copies its text into a
/// <see cref="MysteryObjectiveIssue"/>, so editing the story later never rewrites what the room was
/// already told.
/// </summary>
public sealed class MysteryObjectiveTemplate
{
    public string Id { get; set; } = "";

    /// <summary>Short name for the staff picker.</summary>
    public string Label { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>Set for objectives the phase machine fires on its own. Null for staff-only ones.</summary>
    public MysteryPhase? Phase { get; set; }

    public MysteryAudience Audience { get; set; } = MysteryAudience.Everyone;

    /// <summary>Used when <see cref="Audience"/> is <see cref="MysteryAudience.Faction"/>.</summary>
    public string? FactionId { get; set; }

    /// <summary>
    /// Placeholders the sender fills before this goes out: "target", "zone".
    /// The console renders a picker per slot.
    /// </summary>
    public List<string> Slots { get; set; } = [];
}

/// <summary>Two characters with history. Fires when they scan each other's badge.</summary>
public sealed class MysteryBeef
{
    public string Id { get; set; } = "";
    public string ACharacterId { get; set; } = "";
    public string BCharacterId { get; set; } = "";

    /// <summary>What it is about. Both phones show this.</summary>
    public string Subject { get; set; } = "";

    /// <summary>What A opens with on seeing B.</summary>
    public string ASays { get; set; } = "";

    public string BSays { get; set; } = "";

    public bool Involves(string characterId) =>
        ACharacterId == characterId || BCharacterId == characterId;

    public string? Other(string characterId) =>
        ACharacterId == characterId ? BCharacterId
        : BCharacterId == characterId ? ACharacterId
        : null;
}

/// <summary>The set pieces: the letter, the murder, the study, the ending.</summary>
public sealed class MysteryBeats
{
    /// <summary>The premise, on the invitation and in the briefing.</summary>
    public string Premise { get; set; } = "";

    /// <summary>The letter every guest opens. {name} is filled per character.</summary>
    public string InvitationLetter { get; set; } = "";

    /// <summary>What the screen says when the lights come back up.</summary>
    public string MurderAnnouncement { get; set; } = "";

    /// <summary>The scene in the study, on the screen and on the bloody card.</summary>
    public string StudyScene { get; set; } = "";

    /// <summary>The rules and guidelines every player carries on the Role tab.</summary>
    public string HouseRules { get; set; } = "";

    /// <summary>
    /// How a tampered card reads. <c>{insert}</c> is the framed character's belongings.
    ///
    /// Sentence frames rather than whole clues, because a tamper has to sound like it was always
    /// part of the scene — which is exactly what makes the room argue about whether it was.
    /// </summary>
    public string TamperSubtle { get; set; } = "";

    public string TamperBlatant { get; set; } = "";

    /// <summary>A scrub removes rather than adds, so this replaces the card instead of joining it.</summary>
    public string TamperScrubbed { get; set; } = "";

    /// <summary>Read out at the end, in order, before the winners.</summary>
    public List<string> RevealParagraphs { get; set; } = [];

    public string TownWin { get; set; } = "";
    public string KillerWin { get; set; } = "";

    /// <summary>The guests of the house, all together, when they win. See <see cref="MysteryCharacter.Epilogue"/>.</summary>
    public string VillagerEpilogue { get; set; } = "";
}

// =============================================================================================
//  The play — what the room has done
// =============================================================================================

public sealed class MysteryPlay
{
    /// <summary>The one code for the whole party. Goes on the wall.</summary>
    public string PartyCode { get; set; } = "";

    public List<MysteryCastMember> Cast { get; set; } = [];
    public List<MysteryClueState> ClueStates { get; set; } = [];

    /// <summary>Who scanned which card, when. Written all night, shown from Discussion1.</summary>
    public List<MysteryClueScan> ClueScans { get; set; } = [];

    /// <summary>Who met whom. Badge scans.</summary>
    public List<MysteryMeeting> Meetings { get; set; } = [];

    /// <summary>The conversations: one per pair, three questions each way.</summary>
    public List<MysteryInteraction> Interactions { get; set; } = [];

    /// <summary>The objective queue, append-only.</summary>
    public List<MysteryObjectiveIssue> Objectives { get; set; } = [];

    public List<MysteryTrial> Trials { get; set; } = [];
    public List<MysteryAbilityUse> AbilityUses { get; set; } = [];

    /// <summary>Where the deck is. Only meaningful during Presentation.</summary>
    public int SlideIndex { get; set; }

    /// <summary>Stamped on entering Murder, so a screen opened later does not replay the cinematic.</summary>
    public DateTimeOffset? MurderAt { get; set; }

    /// <summary>
    /// When the current phase began. What the host's countdown is measured from — and only a
    /// countdown: nothing in the game reads the clock to decide anything.
    /// </summary>
    public DateTimeOffset? PhaseEnteredAt { get; set; }

    /// <summary>Who is standing up during the introductions. Only meaningful in that phase.</summary>
    public int IntroIndex { get; set; }

    /// <summary>
    /// How far the ending has been walked. Only meaningful during Reveal.
    ///
    /// In the document rather than in a component, for the same reason <see cref="SlideIndex"/> is:
    /// the host steps it from a phone in their hand and the board has to follow, and a reveal that
    /// lived in the board's own state would restart from the top if the television blinked.
    /// </summary>
    public int RevealIndex { get; set; }

    public MysteryOutcome? Outcome { get; set; }

    public MysteryCastMember? ForCharacter(string characterId) =>
        Cast.FirstOrDefault(c => c.CharacterId == characterId);

    public MysteryCastMember? ForPerson(string personId) =>
        Cast.FirstOrDefault(c => c.PersonId == personId);

    public MysteryClueState? StateFor(string clueId) =>
        ClueStates.FirstOrDefault(c => c.ClueId == clueId);

    /// <summary>Everyone convicted, in the order it happened.</summary>
    public IEnumerable<string> ConvictedCharacterIds =>
        Trials.SelectMany(t => t.ConvictedCharacterIds);
}

/// <summary>One seat: a character, whoever is playing them, and their face.</summary>
public sealed class MysteryCastMember
{
    public string CharacterId { get; set; } = "";

    /// <summary>The <c>RosterPerson.Id</c> holding this part. Null while the seat is empty.</summary>
    public string? PersonId { get; set; }

    /// <summary>
    /// What goes in the QR on this character's name tag, at <c>/m/meet/{token}</c>.
    ///
    /// Deliberately not their join token. A badge is meant to be scanned by other people — that is
    /// the entire mechanic — and a join token signs somebody in as themselves. Printing one on a
    /// name tag would mean anybody who photographed a badge could vote as them.
    /// </summary>
    public string BadgeToken { get; set; } = "";

    /// <summary>"/photos/{id}", from the upload at the door. Empty is fine; the board draws a monogram.</summary>
    public string PhotoUrl { get; set; } = "";

    /// <summary>Set once the letter animation has played, so a reload goes straight to the sheet.</summary>
    public bool LetterOpened { get; set; }

    public DateTimeOffset? JoinedAt { get; set; }
}

/// <summary>What has happened to one of the nine cards.</summary>
public sealed class MysteryClueState
{
    public string ClueId { get; set; } = "";

    /// <summary>
    /// What goes in the QR, at <c>/m/clue/{token}</c>: the card's number, 1 to 9, which is also
    /// what somebody types at /join when the camera will not focus. See
    /// <c>CastingService.NumberTheClues</c> for the trade against an unguessable token.
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>Null while the card reads as written. A card holds at most one.</summary>
    public MysteryTamper? Tamper { get; set; }
}

/// <summary>
/// Somebody got to a card first.
///
/// One per card — a second attempt is refused, which is what stops two jesters turning one clue
/// into a pile of everybody's belongings.
/// </summary>
public sealed class MysteryTamper
{
    /// <summary>subtle, blatant, plant or scrub.</summary>
    public string Mode { get; set; } = "";

    public string ByCharacterId { get; set; } = "";

    /// <summary>
    /// Whose belongings were worked in. The same as <see cref="ByCharacterId"/> for a jester framing
    /// themselves, and null for a scrub, which adds nothing and removes instead.
    /// </summary>
    public string? TargetCharacterId { get; set; }

    public DateTimeOffset At { get; set; }
}

/// <summary>
/// One scan of one card.
///
/// Also the answer to "does this player's copy show the tamper?" — it does exactly when the tamper
/// happened before they scanned. Derived rather than snapshotted, so the two can never disagree.
/// </summary>
public sealed class MysteryClueScan
{
    public string CharacterId { get; set; } = "";
    public string ClueId { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// One badge scan: two people met.
///
/// Pitched to the room as the party's mingle game, which is what it is. It also answers who has been
/// left out of every conversation, which is what the facilitators aim their objectives at.
/// </summary>
public sealed class MysteryMeeting
{
    public string ByCharacterId { get; set; } = "";
    public string MetCharacterId { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// A conversation between two characters: the scanner asks first, then they take turns until each
/// has asked the other's three. One row per pair, read from either side, like a meeting.
/// </summary>
public sealed class MysteryInteraction
{
    public string Id { get; set; } = "";

    /// <summary>Whoever scanned. Asks first.</summary>
    public string ACharacterId { get; set; } = "";
    public string BCharacterId { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<MysteryExchange> Exchanges { get; set; } = [];

    /// <summary>
    /// Who has put the finished conversation away. A conversation used to vanish off both phones
    /// the instant the last question was answered, before either of them had read it — so it
    /// stays on the screen until each side closes it, and each side does that for themselves.
    /// </summary>
    public List<string> ClosedBy { get; set; } = [];

    public bool IsOpen => CompletedAt is null;

    /// <summary>Still on this person's screen: in progress, or finished and not yet put away.</summary>
    public bool ShowingTo(string characterId) =>
        Involves(characterId) && (IsOpen || !ClosedBy.Contains(characterId));

    public bool Involves(string characterId) =>
        ACharacterId == characterId || BCharacterId == characterId;

    public string? Other(string characterId) =>
        ACharacterId == characterId ? BCharacterId
        : BCharacterId == characterId ? ACharacterId
        : null;
}

/// <summary>
/// One question asked and answered.
///
/// The prompt and the answer are written down as they were at the time rather than looked up
/// again later, on purpose: a killer whose story changes between two conversations has been
/// caught in a contradiction, and the transcript is the evidence.
/// </summary>
public sealed class MysteryExchange
{
    public string AskerCharacterId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Answer { get; set; } = "";
    public DateTimeOffset At { get; set; }

    /// <summary>Who has starred this as worth remembering. Each side keeps their own stars.</summary>
    public List<string> StarredBy { get; set; } = [];
}

public enum MysteryAudience
{
    Everyone,
    Faction,

    /// <summary>The twenty-one playing, and none of the four running it.</summary>
    Guests,

    /// <summary>Named individuals, chosen by whoever sent it.</summary>
    Characters
}

/// <summary>
/// One objective, as published.
///
/// The text is copied in at publish time rather than referenced, so rewriting a template at 9pm
/// cannot change what somebody was told at 8:30.
/// </summary>
public sealed class MysteryObjectiveIssue
{
    public string Id { get; set; } = "";

    /// <summary>Which template this came from. Null when a facilitator typed it.</summary>
    public string? TemplateId { get; set; }

    public string Text { get; set; } = "";

    public MysteryAudience Audience { get; set; }
    public string? FactionId { get; set; }
    public List<string> CharacterIds { get; set; } = [];

    public MysteryPhase IssuedInPhase { get; set; }

    /// <summary>Who sent it. Null when the phase machine did.</summary>
    public string? IssuedByPersonId { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>Per recipient, so one shared objective works for twenty-one people.</summary>
    public List<string> CompletedBy { get; set; } = [];
}

/// <summary>
/// One trial.
///
/// Five stages, no clocks: the room votes, and the moment the last living player has locked in the
/// tally resolves on its own. What used to be a timed procedure is now paced by the room, which is
/// the only pacing that works when everybody is holding a drink.
/// </summary>
public sealed class MysteryTrial
{
    public MysteryPhase Phase { get; set; }
    public MysteryTrialStage Stage { get; set; } = MysteryTrialStage.Nominating;

    public List<MysteryVote> Nominations { get; set; } = [];

    /// <summary>Everyone at the cut, ties included.</summary>
    public List<string> NomineeCharacterIds { get; set; } = [];

    /// <summary>Whose turn it is to defend themselves. An index into the nominees.</summary>
    public int SpeakingIndex { get; set; }

    public List<MysteryVote> FinalVotes { get; set; } = [];

    /// <summary>Everyone at the cut, ties included. Not necessarily two.</summary>
    public List<string> ConvictedCharacterIds { get; set; } = [];

    /// <summary>
    /// How many of the convicted have had their card turned over.
    ///
    /// The verdict used to arrive as a wall of KILLER / NOT A KILLER, which is the single biggest
    /// moment in a trial delivered all at once and read by nobody. The host turns them one at a
    /// time, and this is where the room's screen and the four phones agree on how far they have got.
    /// </summary>
    public int VerdictIndex { get; set; }

    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public enum MysteryTrialStage
{
    /// <summary>Everybody votes. The screen shows who has not.</summary>
    Nominating,

    /// <summary>The tally animation. Brief, and purely for the room.</summary>
    Tallying,

    /// <summary>Nominees take turns. Staff step the speaker.</summary>
    Defence,

    /// <summary>Vote again, among the nominees only.</summary>
    FinalVote,

    /// <summary>Jailed, and each card reads KILLER or NON-KILLER.</summary>
    Verdict
}

/// <summary>The five stages in words. Same reason as <see cref="MysteryPhases.Label"/>.</summary>
public static class MysteryTrialStages
{
    public static string Label(MysteryTrialStage stage) => stage switch
    {
        MysteryTrialStage.Nominating => "Nominating",
        MysteryTrialStage.Tallying => "The tally",
        MysteryTrialStage.Defence => "On the stand",
        MysteryTrialStage.FinalVote => "Final vote",
        MysteryTrialStage.Verdict => "The verdict",
        _ => MysteryPhases.Spaced(stage.ToString())
    };
}

/// <summary>One ballot. Stored per voter, so voting again replaces rather than stacks.</summary>
public sealed class MysteryVote
{
    public string VoterCharacterId { get; set; } = "";
    public string TargetCharacterId { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// An ability that has been spent.
///
/// Charges are counted from these rather than decremented on the ability itself, so a shared charge
/// has exactly one place it can be double-spent from — and that place is inside the store's lock.
/// </summary>
public sealed class MysteryAbilityUse
{
    public string AbilityId { get; set; } = "";
    public string ByCharacterId { get; set; } = "";

    /// <summary>The faction the charge came out of, for shared abilities.</summary>
    public string FactionId { get; set; } = "";

    public string? Mode { get; set; }
    public string? TargetCharacterId { get; set; }
    public string? TargetClueId { get; set; }

    /// <summary>What the player was told, so the console can see what the game said.</summary>
    public string? Result { get; set; }

    public DateTimeOffset At { get; set; }
}

/// <summary>
/// How it ended. Killers win on 2+ of 3 surviving, town on 2+ convicted.
/// </summary>
public sealed class MysteryOutcome
{
    public bool TownWon { get; set; }

    /// <summary>On ground truth — a minion taking the blame fools the card, not this.</summary>
    public int KillersConvicted { get; set; }

    /// <summary>What the room was told: killers convicted plus any minion who took the blame.</summary>
    public int ShownKillersConvicted { get; set; }

    /// <summary>
    /// Whether the ending has to admit a KILLER card was a lie: the room would have won on what it
    /// was shown and did not win on the truth. Otherwise the associate is never named as one.
    /// </summary>
    public bool RevealDecoy { get; set; }

    /// <summary>Jesters who got themselves convicted, and Brauns who outlived a convicted rival.</summary>
    public List<string> PersonalWinnerCharacterIds { get; set; } = [];

    public DateTimeOffset EndedAt { get; set; }
}

// =============================================================================================
//  Content gaps
// =============================================================================================

/// <summary>
/// Whether a field has actually been written yet.
///
/// The story ships structurally complete and textually empty: every unwritten string is a row of
/// dots. One predicate then does four jobs — the editor draws the field as a blank to fill in, the
/// Content gaps panel counts what is left, a player-facing surface leaves the line out rather than
/// showing dots to the room, and <c>grep '"\.\+"' data/braun-manor/*.json</c> lists the lot.
/// </summary>
public static class MysteryText
{
    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().All(c => c == '.');

    /// <summary>The value if somebody wrote it, otherwise null. For surfaces that omit rather than show.</summary>
    public static string? Written(string? value) =>
        IsPlaceholder(value) ? null : value;

    /// <summary>Lines that have actually been written, in order.</summary>
    public static IEnumerable<string> WrittenOnly(IEnumerable<string>? lines) =>
        (lines ?? []).Where(l => !IsPlaceholder(l));
}
