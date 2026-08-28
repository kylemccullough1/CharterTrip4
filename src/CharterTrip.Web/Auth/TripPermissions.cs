using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CharterTrip.Web.Auth;

/// <summary>
/// What the person looking at the screen is allowed to do. Cascaded from MainLayout so every
/// component can ask without injecting anything.
/// </summary>
/// <param name="IsAdmin">Committee. Can edit everything and see the money.</param>
/// <param name="DisplayName">Who to greet. "Guest" when nobody is signed in.</param>
/// <param name="PersonId">Their <c>RosterPerson.Id</c>, when they signed in as themselves.</param>
/// <param name="TeamId">Their team, from a buzzer code or from their own roster entry.</param>
/// <param name="IsBuzzerHost">Holding the Jeopardy host code.</param>
public sealed record TripPermissions(
    bool IsAdmin,
    string DisplayName,
    string? PersonId = null,
    string? TeamId = null,
    bool IsBuzzerHost = false)
{
    public bool CanEdit => IsAdmin;

    /// <summary>Admin-only areas: money, the full roster, clue text.</summary>
    public bool CanSeeAdminAreas => IsAdmin;

    /// <summary>
    /// True when this is a named person rather than an anonymous guest or a shared code.
    ///
    /// This is the one that matters for the murder mystery: a game where twenty-one people each
    /// hold different secrets cannot be played by "whoever has the link".
    /// </summary>
    public bool IsPlayer => PersonId is not null;

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
/// There are three ways to be signed in and this reads all of them off claims rather than off the
/// mere fact of being authenticated:
///
///   the committee's username and password   → admin, no person
///   a person's own /join/{token} link       → that person, admin only if the roster says so
///   a Jeopardy buzzer or host code          → a team or the host job, no person
///
/// Being authenticated used to be the same fact as being an admin, because there was one account.
/// It is not any more: twenty-one guests will hold their own links, and a guest whose cookie made
/// them an admin would be able to read the murder mystery's solution.
/// </summary>
public sealed class CookieCurrentUser(AuthenticationStateProvider provider) : ICurrentUser
{
    public async ValueTask<TripPermissions> GetAsync()
    {
        var state = await provider.GetAuthenticationStateAsync();
        var user = state.User;

        if (user.Identity?.IsAuthenticated != true) return TripPermissions.Guest;

        return new TripPermissions(
            IsAdmin: user.IsInRole(TripRoles.Admin),
            DisplayName: user.Identity.Name ?? "Guest",
            PersonId: Claim(user, TripClaims.PersonId),
            TeamId: Claim(user, TripClaims.TeamId),
            IsBuzzerHost: user.IsInRole(TripRoles.BuzzerHost));
    }

    private static string? Claim(ClaimsPrincipal user, string type)
    {
        var value = user.FindFirst(type)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
