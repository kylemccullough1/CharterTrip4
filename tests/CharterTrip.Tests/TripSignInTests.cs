using System.Security.Claims;
using CharterTrip.Core.Models;
using CharterTrip.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace CharterTrip.Tests;

/// <summary>
/// Who a cookie says you are.
///
/// This used to be one question — authenticated or not — because there was one account, and being
/// signed in was the same fact as being an admin. It is three questions now, and the one that
/// matters most is the negative: a guest holding their own link must not come back as an admin,
/// or twenty-one people can read the murder mystery's solution.
/// </summary>
public class TripSignInTests
{
    private static TripPermissions Read(ClaimsPrincipal principal) =>
        new CookieCurrentUser(new StubAuthState(principal)).GetAsync().GetAwaiter().GetResult();

    private static RosterPerson Person(TripRole role = TripRole.Member) => new()
    {
        Id = "p-1",
        Name = "Sharkeisha",
        TeamId = "team-2",
        JoinToken = "ACDEFGHJKM",
        Role = role
    };

    [Fact]
    public void Nobody_signed_in_is_a_guest()
    {
        var permissions = Read(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(permissions.IsAdmin);
        Assert.False(permissions.IsPlayer);
        Assert.Null(permissions.PersonId);
        Assert.Null(permissions.TeamId);
        Assert.False(permissions.IsBuzzerHost);
        Assert.Equal("Guest", permissions.DisplayName);
    }

    [Fact]
    public void The_committee_password_is_an_admin_but_not_a_person()
    {
        var permissions = Read(TripSignIn.ForCommittee());

        Assert.True(permissions.IsAdmin);
        Assert.True(permissions.CanEdit);
        Assert.Equal("Committee", permissions.DisplayName);

        // The shared session cannot play the murder mystery — it is not anybody in particular,
        // and the game needs twenty-one distinct people.
        Assert.False(permissions.IsPlayer);
        Assert.Null(permissions.PersonId);
    }

    [Fact]
    public void A_guest_with_their_own_link_is_a_player_and_not_an_admin()
    {
        var permissions = Read(TripSignIn.ForPerson(Person()));

        Assert.True(permissions.IsPlayer);
        Assert.Equal("p-1", permissions.PersonId);
        Assert.Equal("Sharkeisha", permissions.DisplayName);
        Assert.Equal("team-2", permissions.TeamId);

        // The whole point of phase 2. Being signed in is no longer the same thing as being trusted.
        Assert.False(permissions.IsAdmin);
        Assert.False(permissions.CanEdit);
        Assert.False(permissions.CanSeeAdminAreas);
    }

    [Fact]
    public void An_organizer_with_their_own_link_is_both()
    {
        var permissions = Read(TripSignIn.ForPerson(Person(TripRole.Admin)));

        Assert.True(permissions.IsPlayer);
        Assert.True(permissions.IsAdmin);
        Assert.Equal("p-1", permissions.PersonId);
    }

    [Fact]
    public void A_person_with_no_team_still_signs_in()
    {
        var person = Person();
        person.TeamId = "";

        var permissions = Read(TripSignIn.ForPerson(person));

        Assert.True(permissions.IsPlayer);
        Assert.Null(permissions.TeamId);
    }

    [Fact]
    public void A_buzzer_code_is_a_team_and_nobody_in_particular()
    {
        var permissions = Read(TripSignIn.ForBuzzerTeam(new Team { Id = "team-3", Name = "The Snails" }));

        Assert.Equal("team-3", permissions.TeamId);
        Assert.Equal("The Snails", permissions.DisplayName);
        Assert.False(permissions.IsPlayer);
        Assert.False(permissions.IsAdmin);
        Assert.False(permissions.IsBuzzerHost);
    }

    [Fact]
    public void The_host_code_is_the_host_job_and_not_an_admin()
    {
        var permissions = Read(TripSignIn.ForBuzzerHost());

        Assert.True(permissions.IsBuzzerHost);

        // Holding the buzzer host code should run the buzzer, not unlock the budget.
        Assert.False(permissions.IsAdmin);
        Assert.False(permissions.CanSeeAdminAreas);
        Assert.False(permissions.IsPlayer);
        Assert.Null(permissions.TeamId);
    }

    [Fact]
    public void An_authenticated_cookie_carrying_no_claims_is_not_an_admin()
    {
        // The old behaviour, guarded against coming back: any authenticated identity used to mean
        // admin. A cookie from before this change, or one carrying only a name, must not.
        var stale = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "Somebody")], "cookie"));

        var permissions = Read(stale);

        Assert.False(permissions.IsAdmin);
        Assert.False(permissions.IsPlayer);
        Assert.Equal("Somebody", permissions.DisplayName);
    }

    private sealed class StubAuthState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }
}
