using System.Text.Json.Serialization;

namespace CharterTrip.Core.Mystery.Script;

/// <summary>
/// The shape of the evening, from <c>rounds.json</c>.
/// </summary>
public sealed record ScriptRoundBook
{
    public IReadOnlyList<ScriptRound> Rounds { get; init; } = [];

    public ScriptTrialProcedure TrialProcedure { get; init; } = new();

    /// <summary>Why every non-killer conviction reads as GUEST during play.</summary>
    public string RevealCardNote { get; init; } = "";

    /// <summary>The authored total. <see cref="ScheduledMinutes"/> is the arithmetic; the script
    /// test asserts they agree, because a generator that trusts a stale summary runs late.</summary>
    public int TotalRuntimeMinutes { get; init; }

    /// <summary>What the rounds actually add up to.</summary>
    public int ScheduledMinutes => Rounds.Sum(r => r.Minutes);

    public ScriptRound? ById(string id) => Rounds.FirstOrDefault(r => r.Id == id);

    /// <summary>The three trials, in order.</summary>
    public IEnumerable<ScriptRound> Trials => Rounds.Where(r => r.IsTrial);
}

/// <summary>
/// One block of the evening. A round is either something the room does (with
/// <see cref="Screen"/> text and <see cref="Mechanics"/>) or a trial (with
/// <see cref="Convictions"/> slots to fill).
/// </summary>
public sealed record ScriptRound
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Minutes { get; init; }

    /// <summary>What the main screen says during this round.</summary>
    public string? Screen { get; init; }

    /// <summary>What is happening under the hood, for whoever is running it.</summary>
    public IReadOnlyList<string> Mechanics { get; init; } = [];

    /// <summary>Abilities that become available at the start of this round.</summary>
    public IReadOnlyList<string> Unlocks { get; init; } = [];

    /// <summary>Non-null on trial rounds: how many people get convicted here. Always 2.</summary>
    public int? Convictions { get; init; }

    public string? Procedure { get; init; }

    public bool IsTrial => Convictions is not null;
}

/// <summary>
/// How a trial runs, phase by phase.
///
/// The two cuts are where a trial can wedge, and both have their tie rule written down: everyone
/// tied at the nomination cut is nominated, and a tie at the conviction cut revotes and then falls
/// back to the earlier open tally. Implement both or a trial can hang with the room standing still.
/// </summary>
public sealed record ScriptTrialProcedure
{
    [JsonPropertyName("phase_1")] public string Phase1 { get; init; } = "";
    [JsonPropertyName("phase_2")] public string Phase2 { get; init; } = "";
    [JsonPropertyName("phase_3")] public string Phase3 { get; init; } = "";
    [JsonPropertyName("phase_4")] public string Phase4 { get; init; } = "";
    [JsonPropertyName("phase_5")] public string Phase5 { get; init; } = "";
    [JsonPropertyName("phase_6")] public string Phase6 { get; init; } = "";

    /// <summary>Town wins at 2+ killers convicted, but that is evaluated after the third trial.
    /// Play stops early only on a clean sweep of all three.</summary>
    public string EarlyEnd { get; init; } = "";

    /// <summary>The six phases in order, for a screen that walks through them.</summary>
    public IReadOnlyList<string> Phases => [Phase1, Phase2, Phase3, Phase4, Phase5, Phase6];
}
