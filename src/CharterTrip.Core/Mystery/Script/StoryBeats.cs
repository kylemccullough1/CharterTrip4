namespace CharterTrip.Core.Mystery.Script;

/// <summary>
/// Every sentence the game can say, from <c>story_beats.json</c>.
///
/// <see cref="AssemblyRules"/> is the contract this whole design rests on: the compiler composes
/// these blocks and fills their placeholders, and it never authors a word. That is what keeps a
/// game with millions of distinct deals reviewable in one sitting.
/// </summary>
public sealed record ScriptStoryBeats
{
    public ScriptSpine Spine { get; init; } = new();

    /// <summary>Keyed by the character id who could have done it this way. Five methods, so five keys.</summary>
    public IReadOnlyDictionary<string, ScriptMethodBeat> MethodBeats { get; init; } =
        new Dictionary<string, ScriptMethodBeat>();

    /// <summary>Keyed by route id: corridor, window, side_path.</summary>
    public IReadOnlyDictionary<string, ScriptAccessBeat> AccessBeats { get; init; } =
        new Dictionary<string, ScriptAccessBeat>();

    /// <summary>The one who gave the order. Only one, so it is not a dictionary.</summary>
    public ScriptSignatureBeat SignatureBeats { get; init; } = new();

    /// <summary>What the screen says when someone is convicted, keyed by what they turned out to be.</summary>
    public IReadOnlyDictionary<string, string> ConvictionReveals { get; init; } =
        new Dictionary<string, string>();

    /// <summary>The endgame texts, keyed by outcome: town_win, killer_win, jester_reveal, and so on.</summary>
    public IReadOnlyDictionary<string, string> EndgameReveals { get; init; } =
        new Dictionary<string, string>();

    public ScriptAssemblyRules AssemblyRules { get; init; } = new();

    public ScriptTamperSystem TamperSystem { get; init; } = new();
}

/// <summary>
/// The fixed story: what happened, what the guests are told, and what the study looks like after.
/// True in every game regardless of the deal.
/// </summary>
public sealed record ScriptSpine
{
    public string Premise { get; init; } = "";

    /// <summary>The invitation letter, with per-character hooks filled in. Braun inviting the
    /// person he is about to ruin is the intended energy for every one of them.</summary>
    public string InvitationTemplate { get; init; } = "";

    public string InvitationNotes { get; init; } = "";
    public string MurderAnnouncement { get; init; } = "";

    /// <summary>The study scene minus its method flavour, which the deal appends.</summary>
    public string StudySceneBase { get; init; } = "";

    /// <summary>Keyed by round id — r2, r3, r4.</summary>
    public IReadOnlyDictionary<string, string> RoundIntros { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// How the murder was done, for one possible means killer. Only one of the five is ever true.
/// </summary>
public sealed record ScriptMethodBeat
{
    /// <summary>Appended to the study scene so the room can see the method without being told it.</summary>
    public string SceneFlavor { get; init; } = "";

    /// <summary>What the means killer reads on their own phone.</summary>
    public string Briefing { get; init; } = "";

    /// <summary>What the endgame says about it.</summary>
    public string Reveal { get; init; } = "";
}

/// <summary>
/// How the killer got into the study, for one of the three routes.
/// </summary>
public sealed record ScriptAccessBeat
{
    public string Briefing { get; init; } = "";
    public string Reveal { get; init; } = "";
}

/// <summary>
/// The order that started it. The signature killer never touched anyone.
/// </summary>
public sealed record ScriptSignatureBeat
{
    public string Briefing { get; init; } = "";
    public string Reveal { get; init; } = "";
}

/// <summary>
/// The composition recipes, written as the formulas they are. The compiler implements these
/// literally; the note at the end is the rule that keeps it honest.
/// </summary>
public sealed record ScriptAssemblyRules
{
    public string StudyScene { get; init; } = "";
    public string KillerBriefingAccess { get; init; } = "";
    public string KillerBriefingMeans { get; init; } = "";
    public string KillerBriefingSignature { get; init; } = "";
    public string CoverStoryTemplate { get; init; } = "";
    public string WitnessStatements { get; init; } = "";
    public string HerringExoneration { get; init; } = "";

    /// <summary>"No field is written at runtime." An optional AI pass may restyle voice; it may
    /// not add, remove, or alter facts.</summary>
    public string Note { get; init; } = "";
}

/// <summary>
/// Framing someone with their own belongings, and erasing yourself.
///
/// Every tamper requires physically scanning the clue's QR — you have to walk there, visibly,
/// which is the cost that makes the ability fair.
/// </summary>
public sealed record ScriptTamperSystem
{
    public string InsertRenderSubtle { get; init; } = "";
    public string InsertRenderBlatant { get; init; } = "";
    public string InsertRenderPlant { get; init; } = "";

    /// <summary>What a scrubbed clue reads instead of its trace.</summary>
    public string ScrubRender { get; init; } = "";

    public string ForensicsResult { get; init; } = "";
    public string ForensicsClean { get; init; } = "";

    public IReadOnlyList<string> Rules { get; init; } = [];

    /// <summary>The render template for a given tamper mode: subtle, blatant, or plant.</summary>
    public string? RenderFor(string mode) => mode switch
    {
        "subtle" => InsertRenderSubtle,
        "blatant" => InsertRenderBlatant,
        "plant" => InsertRenderPlant,
        _ => null
    };
}
