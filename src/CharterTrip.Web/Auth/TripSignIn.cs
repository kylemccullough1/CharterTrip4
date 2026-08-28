using System.Security.Claims;
using CharterTrip.Core.Models;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CharterTrip.Web.Auth;

/// <summary>
/// Builds the identity that goes into the cookie.
///
/// One place for all four ways in, so that the sign-in page and the code page cannot disagree
/// about what a signed-in person looks like. <see cref="CookieCurrentUser"/> is the only reader,
/// and these are the only writers.
/// </summary>
public static class TripSignIn
{
    /// <summary>The committee's shared username and password. An admin, but not any one person —
    /// which is why the murder mystery cannot be played from this session.</summary>
    public static ClaimsPrincipal ForCommittee() =>
        Build(
            [
                new Claim(ClaimTypes.Name, "Committee"),
                new Claim(ClaimTypes.Role, TripRoles.Admin)
            ]);

    /// <summary>
    /// Somebody following their own link. This is the identity every game derives from.
    ///
    /// Admin comes from the roster rather than from the act of signing in: the four organizers
    /// carry <see cref="TripRole.Admin"/>, and the other twenty-one do not.
    /// </summary>
    public static ClaimsPrincipal ForPerson(RosterPerson person)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, person.Name),
            new(TripClaims.PersonId, person.Id)
        };

        // Their team comes along for free, so a person signed in as themselves can use the buzzer
        // without also needing to type the team's code off the wall.
        if (!string.IsNullOrWhiteSpace(person.TeamId))
            claims.Add(new Claim(TripClaims.TeamId, person.TeamId));

        if (person.Role == TripRole.Admin)
            claims.Add(new Claim(ClaimTypes.Role, TripRoles.Admin));

        return Build(claims);
    }

    /// <summary>A Jeopardy buzzer code: a team, shared by whoever typed it. No person.</summary>
    public static ClaimsPrincipal ForBuzzerTeam(Team team) =>
        Build(
            [
                new Claim(ClaimTypes.Name, team.Name),
                new Claim(TripClaims.TeamId, team.Id)
            ]);

    /// <summary>The Jeopardy host code: a job for one evening.</summary>
    public static ClaimsPrincipal ForBuzzerHost() =>
        Build(
            [
                new Claim(ClaimTypes.Name, "Host"),
                new Claim(ClaimTypes.Role, TripRoles.BuzzerHost)
            ]);

    private static ClaimsPrincipal Build(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
}
