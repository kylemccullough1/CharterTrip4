namespace CharterTrip.Core.Models;

public sealed class Team
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Lead { get; set; } = "";
    public string Color { get; set; } = "#d4af37";
    public string Headband { get; set; } = "";
    public string? PhotoId { get; set; }
}

/// <summary>
/// One person on the trip. This is the single source of truth for team membership —
/// teams do not carry their own roster lists.
/// </summary>
public sealed class RosterPerson
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TeamId { get; set; } = "";

    /// <summary>Phase 2: the secret in their personal /join/{token} link.</summary>
    public string? JoinToken { get; set; }

    /// <summary>Phase 2: committee members get Admin.</summary>
    public TripRole Role { get; set; } = TripRole.Member;
}

public enum TripRole
{
    Member,
    Admin
}
