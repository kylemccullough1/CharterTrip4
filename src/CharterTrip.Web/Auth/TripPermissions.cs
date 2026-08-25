using Microsoft.AspNetCore.Components.Authorization;

namespace CharterTrip.Web.Auth;

/// <summary>
/// What the person looking at the screen is allowed to do. Cascaded from MainLayout so every
/// component can ask without injecting anything.
/// </summary>
public sealed record TripPermissions(bool IsAdmin, string DisplayName, string? PersonId = null)
{
    public bool CanEdit => IsAdmin;

    /// <summary>Admin-only areas: money, the full roster, clue text.</summary>
    public bool CanSeeAdminAreas => IsAdmin;

    /// <summary>Nobody is signed in. The default for every guest, and for a component with no cascade.</summary>
    public static readonly TripPermissions Guest = new(IsAdmin: false, DisplayName: "Guest");
}

public interface ICurrentUser
{
    ValueTask<TripPermissions> GetAsync();
}

/// <summary>
/// Reads the signed-in identity out of the authentication cookie.
///
/// Asking <see cref="AuthenticationStateProvider"/> rather than IHttpContextAccessor is the whole
/// trick on Blazor Server: there is an HttpContext during the first render and never again, so a
/// component that consulted it directly would see an admin on load and a guest the moment the
/// circuit took over. The state provider captures the identity when the circuit opens and holds
/// it for the life of the connection.
///
/// There is one account, and it is the committee's, so being authenticated is the same thing as
/// being an admin.
/// </summary>
public sealed class CookieCurrentUser(AuthenticationStateProvider provider) : ICurrentUser
{
    public async ValueTask<TripPermissions> GetAsync()
    {
        var state = await provider.GetAuthenticationStateAsync();
        var identity = state.User.Identity;

        return identity?.IsAuthenticated == true
            ? new TripPermissions(IsAdmin: true, DisplayName: identity.Name ?? "Committee")
            : TripPermissions.Guest;
    }
}
