namespace CharterTrip.Web.Auth;

/// <summary>
/// What the person looking at the screen is allowed to do. Cascaded from MainLayout so every
/// component can ask without injecting anything.
///
/// Phase 1 hands out admin to everyone. The point of this type existing now is that pages are
/// written against it from day one, so switching on real logins later changes the answer
/// without changing a single page.
/// </summary>
public sealed record TripPermissions(bool IsAdmin, string DisplayName, string? PersonId = null)
{
    public bool CanEdit => IsAdmin;

    /// <summary>Admin-only areas: money, the full roster, clue text.</summary>
    public bool CanSeeAdminAreas => IsAdmin;
}

public interface ICurrentUser
{
    TripPermissions Get();
}

/// <summary>Phase 1 only. Replaced in phase 2 by the join-link cookie.</summary>
public sealed class AlwaysAdminUser : ICurrentUser
{
    public TripPermissions Get() => new(IsAdmin: true, DisplayName: "Committee");
}
