namespace CharterTrip.Core.Models;

/// <summary>
/// Murder at Braun Manor, as it exists in trip.json.
///
/// Two things live here and nothing else: <see cref="Deal"/> — what this particular game
/// generated — and the live state, which is what the room has done to it since. The authored
/// content is not here. It is embedded in the assembly and reached through MysteryScript, because
/// 110 KB of prose rewritten by the debounced writer on every vote would be both wasteful and the
/// end of being able to read the trip in a diff.
///
/// The deal stores <em>choices</em>, not sentences. Who is where, who is guilty, which route, which
/// clue sits in which room. Every word a player reads is composed from those choices plus the
/// script, on demand — so this document stays small, and a fix to a piece of prose does not require
/// regenerating anybody's game.
/// </summary>
public sealed class MysteryState
{
    /// <summary>The game is running. False before the deal and after the reveal.</summary>
    public bool Active { get; set; }

    /// <summary>Index into <c>rounds.json</c>. -1 before the first round starts.</summary>
    public int CurrentRoundIndex { get; set; } = -1;

    /// <summary>
    /// The one code for the whole party, shown on the screen and typed by everybody.
    ///
    /// There used to be twenty-one personal links to hand out at the door. One code is a great deal
    /// less to go wrong on the night: it goes on the wall, everybody types it, picks their own name,
    /// and is dealt a character. Nothing is printed per guest and nobody can be handed the wrong
    /// envelope.
    /// </summary>
    public string PartyCode { get; set; } = "";

    /// <summary>Null until the host deals. Everything else here is meaningless without it.</summary>
    public MysteryDeal? Deal { get; set; }

    /// <summary>The nine physical clue cards, and what has happened to each.</summary>
    public List<MysteryClue> Clues { get; set; } = [];

    /// <summary>One per trial round, created when the trial opens.</summary>
    public List<MysteryTrial> Trials { get; set; } = [];

    /// <summary>Badge scans, in order. The interaction graph the prompt engine reads.</summary>
    public List<MysteryScan> Scans { get; set; } = [];

    /// <summary>Every ability fired, which is also how remaining charges are counted.</summary>
    public List<MysteryAbilityUse> AbilityUses { get; set; } = [];

    /// <summary>Set when the game ends, so the reveal screen does not recompute a verdict.</summary>
    public MysteryOutcome? Outcome { get; set; }

    /// <summary>Convicted characters in the order they were convicted, across all trials.</summary>
    public IEnumerable<string> ConvictedCharacterIds =>
        Trials.SelectMany(t => t.ConvictedCharacterIds);

    public MysteryCastMember? ForCharacter(string characterId) =>
        Deal?.Cast.FirstOrDefault(c => c.CharacterId == characterId);

    public MysteryCastMember? ForPerson(string personId) =>
        Deal?.Cast.FirstOrDefault(c => c.PersonId == personId);
}

/// <summary>
/// What the generator decided. Reproducible from <see cref="Seed"/> and the roster alone, which is
/// why <c>?seed=1234</c> can replay a game without twenty-one phones.
/// </summary>
public sealed class MysteryDeal
{
    public int Seed { get; set; }
    public DateTimeOffset GeneratedUtc { get; set; }

    /// <summary>One row per character: who plays them, where they were, what they are.</summary>
    public List<MysteryCastMember> Cast { get; set; } = [];

    /// <summary>Which of the three routes the access killer came by.</summary>
    public string AccessRoute { get; set; } = "";

    /// <summary>
    /// Extra observations granted across a zone boundary — "seen from the doorway".
    ///
    /// The fifth post-simulation rule: a killer with exactly one co-located witness gets a second
    /// thread from an adjacent room, so no killer is ever one conversation away from invisible.
    /// </summary>
    public List<MysterySighting> CrossZoneSightings { get; set; } = [];

    /// <summary>Killers by guilt slot, for composing the study scene and the reveal.</summary>
    public string? KillerFor(string slot) =>
        Cast.FirstOrDefault(c => c.GuiltSlot == slot)?.CharacterId;

    public IEnumerable<MysteryCastMember> Killers => Cast.Where(c => c.GuiltSlot is not null);
    public IEnumerable<MysteryCastMember> Herrings => Cast.Where(c => c.IsHerring);

    public IEnumerable<MysteryCastMember> InFaction(string factionId) =>
        Cast.Where(c => c.FactionId == factionId);

    public IEnumerable<MysteryCastMember> InZone(string zoneId) =>
        Cast.Where(c => c.ZoneId == zoneId);
}

/// <summary>
/// One character, as dealt. Twenty-one of these are the whole game.
/// </summary>
public sealed class MysteryCastMember
{
    public string CharacterId { get; set; } = "";

    /// <summary>The <c>RosterPerson.Id</c> playing them. Null if the seat is uncast.</summary>
    public string? PersonId { get; set; }

    /// <summary>
    /// What goes in the QR on this character's name tag, at <c>/m/meet/{token}</c>.
    ///
    /// Deliberately not their join token. A badge is meant to be scanned by other people — that is
    /// the entire mechanic — and a join token is the credential that signs somebody in as
    /// themselves. Printing one on a name tag would mean anybody who photographed a badge could
    /// become that player, read their secrets and vote as them.
    /// </summary>
    public string BadgeToken { get; set; } = "";

    /// <summary>Where they spent the evening. Drives their witness statements.</summary>
    public string ZoneId { get; set; } = "";

    /// <summary>Their faction id from <c>factions.json</c>.</summary>
    public string FactionId { get; set; } = "";

    /// <summary>access, means, or signature for the three killers. Null for everybody else.</summary>
    public string? GuiltSlot { get; set; }

    /// <summary>An innocent showing their guilty reading. Indistinguishable from a killer by design.</summary>
    public bool IsHerring { get; set; }

    /// <summary>
    /// The other claimant, for the two inheritance players. Each is the other's rival, and the
    /// pairing is stored rather than derived so that the reveal cannot disagree with the briefing.
    /// </summary>
    public string? RivalCharacterId { get; set; }

    /// <summary>True if the room sees this character's guilty text — killer or red herring.</summary>
    public bool ShowsGuilty => GuiltSlot is not null || IsHerring;

    public bool IsKiller => GuiltSlot is not null;
}

/// <summary>
/// One character seeing another from the next room over.
/// </summary>
public sealed class MysterySighting
{
    /// <summary>Who saw it — a player in a zone adjacent to the subject's.</summary>
    public string ObserverCharacterId { get; set; } = "";

    public string SubjectCharacterId { get; set; } = "";
}

/// <summary>
/// A printed clue card in a room, and everything that has happened to it.
/// </summary>
public sealed class MysteryClue
{
    public string Id { get; set; } = "";

    /// <summary>
    /// What goes in the QR code, at <c>/m/clue/{token}</c>.
    ///
    /// Unguessable on purpose. Nine sequential ids would let a bored player read all nine clues
    /// from the sofa, and walking to the room is the entire mechanic.
    /// </summary>
    public string Token { get; set; } = "";

    public string ZoneId { get; set; } = "";

    /// <summary>Whose trace this is. Null for a neutral spine clue filling an otherwise empty room.</summary>
    public string? TraceCharacterId { get; set; }

    /// <summary>For a spine clue, which one — so the text can be composed without a character.</summary>
    public string? SpineClueId { get; set; }

    public bool Found { get; set; }
    public string? FoundByCharacterId { get; set; }
    public DateTimeOffset? FoundAt { get; set; }

    /// <summary>Null while the clue reads as authored.</summary>
    public MysteryTamper? Tamper { get; set; }
}

/// <summary>
/// Somebody got to a clue first.
///
/// A clue holds at most one of these — a second attempt is refused silently, which is what stops
/// two jesters turning one card into a pile of everybody's belongings.
/// </summary>
public sealed class MysteryTamper
{
    /// <summary>subtle, blatant, plant, or scrub.</summary>
    public string Mode { get; set; } = "";

    public string ByCharacterId { get; set; } = "";

    /// <summary>Whose belongings were worked in. Equals <see cref="ByCharacterId"/> for a jester
    /// framing themselves; null for a scrub, which adds nothing.</summary>
    public string? TargetCharacterId { get; set; }

    public DateTimeOffset At { get; set; }
}

public enum MysteryTrialPhase
{
    /// <summary>Phase 1: everyone votes, tally shown as silhouettes.</summary>
    OpenVote,

    /// <summary>Phases 2-3: the top four are nominated and get their final words.</summary>
    Defence,

    /// <summary>Phase 4: non-nominees vote among the nominees.</summary>
    FinalVote,

    /// <summary>Phase 5-6: two convicted, cards on the screen.</summary>
    Revealed
}

/// <summary>
/// One trial. The two cuts — nomination and conviction — are where a trial can wedge, so both
/// tie rules from <c>rounds.json</c> are implemented against this.
/// </summary>
public sealed class MysteryTrial
{
    public string RoundId { get; set; } = "";
    public MysteryTrialPhase Phase { get; set; } = MysteryTrialPhase.OpenVote;

    public List<MysteryVote> OpenVotes { get; set; } = [];

    /// <summary>Top four, plus anybody tied at the cut.</summary>
    public List<string> NomineeCharacterIds { get; set; } = [];

    public List<MysteryVote> FinalVotes { get; set; } = [];

    /// <summary>Top two of the final vote, plus tie resolution.</summary>
    public List<string> ConvictedCharacterIds { get; set; } = [];

    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>One ballot. Stored per voter so a second vote replaces rather than adds.</summary>
public sealed class MysteryVote
{
    public string VoterCharacterId { get; set; } = "";
    public string TargetCharacterId { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// One badge scan: an edge in the interaction graph.
///
/// Pitched to the room as a mingle game and never explained as telemetry, which is what it is —
/// it feeds underserved detection, contradiction routing, and the endgame stats.
/// </summary>
public sealed class MysteryScan
{
    public string ByCharacterId { get; set; } = "";
    public string MetCharacterId { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// An ability that has been spent.
///
/// Charges are counted from these rather than decremented on the ability, so a shared charge has
/// exactly one place it can be double-spent from — and that place is inside the store's lock.
/// </summary>
public sealed class MysteryAbilityUse
{
    public string AbilityId { get; set; } = "";
    public string ByCharacterId { get; set; } = "";

    /// <summary>The faction the charge came out of, for shared abilities.</summary>
    public string FactionId { get; set; } = "";

    /// <summary>Which mode, for abilities that make the player choose.</summary>
    public string? Mode { get; set; }

    public string? TargetCharacterId { get; set; }
    public string? TargetClueId { get; set; }

    /// <summary>What the player was told, so the host console can see what the game said.</summary>
    public string? Result { get; set; }

    public DateTimeOffset At { get; set; }
}

/// <summary>
/// How it ended. Ruleset B: killers win on 2+ of 3 surviving, town on 2+ convicted.
/// </summary>
public sealed class MysteryOutcome
{
    public bool TownWon { get; set; }

    /// <summary>Killers convicted on ground truth — blame-take fools the card, not this.</summary>
    public int KillersConvicted { get; set; }

    /// <summary>Characters who scored their personal win: jesters convicted, Brauns who outlived
    /// a convicted rival.</summary>
    public List<string> PersonalWinnerCharacterIds { get; set; } = [];

    public DateTimeOffset EndedAt { get; set; }
}
