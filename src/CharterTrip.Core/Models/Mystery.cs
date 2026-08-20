namespace CharterTrip.Core.Models;

/// <summary>
/// Murder at West Egg Manor. Phase 1 stores the reference material only; phase 3 turns
/// on casting, round control and the per-phone character cards.
/// </summary>
public sealed class MysteryState
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Setting { get; set; } = "";
    public string Hook { get; set; } = "";
    public string Premise { get; set; } = "";
    public string HostNote { get; set; } = "";

    public List<string> CrimeSteps { get; set; } = [];
    public List<string> ExampleSecrets { get; set; } = [];
    public List<string> ClueTypes { get; set; } = [];
    public List<MysteryRound> Rounds { get; set; } = [];
    public List<MysteryCharacter> Characters { get; set; } = [];
    public List<ClueCard> Clues { get; set; } = [];

    // --- live game state (phase 3) ---
    public bool Active { get; set; }
    public bool CastRevealed { get; set; }
    public bool VotingOpen { get; set; }
    public int CurrentRound { get; set; } = -1;
}

public sealed class MysteryRound
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class MysteryCharacter
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsConspirator { get; set; }
    public bool IsMastermind { get; set; }

    /// <summary>Shown to conspirators only — how they contributed to the murder.</summary>
    public string Brief { get; set; } = "";

    // Filled in by the host before the night, delivered privately to one phone.
    public string Secret { get; set; } = "";
    public string Motive { get; set; } = "";
    public string Protecting { get; set; } = "";
    public string SuspiciousActivity { get; set; } = "";
    public string Objective1 { get; set; } = "";
    public string Objective2 { get; set; } = "";

    /// <summary>RosterPerson.Id, once the cast is assigned.</summary>
    public string? AssignedPersonId { get; set; }
}

public sealed class ClueCard
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string Type { get; set; } = "";
    public int ReleaseRound { get; set; }
    public bool Released { get; set; }

    /// <summary>Null means public. Set to hand a clue privately to one character.</summary>
    public string? ToCharacterId { get; set; }
}
