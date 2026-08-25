using CharterTrip.Web.Auth;

namespace CharterTrip.Tests;

/// <summary>
/// What a guest is allowed to see and where a sign-in is allowed to send them.
/// </summary>
public class GuestVisibilityTests
{
    private static readonly TripPermissions Admin = new(IsAdmin: true, DisplayName: "Committee");

    [Fact]
    public void Guests_do_not_get_teams_games_or_data_in_the_menu()
    {
        var labels = NavTree.For(TripPermissions.Guest).Select(e => e.Label).ToList();

        Assert.DoesNotContain("Teams", labels);
        Assert.DoesNotContain("Games", labels);
        Assert.DoesNotContain("Data", labels);
    }

    [Fact]
    public void Guests_still_get_the_parts_of_the_weekend_that_concern_them()
    {
        var labels = NavTree.For(TripPermissions.Guest).Select(e => e.Label).ToList();

        Assert.Equal(["Home", "Itinerary", "Venue"], labels);
    }

    [Fact]
    public void The_committee_gets_everything()
    {
        var labels = NavTree.For(Admin).Select(e => e.Label).ToList();

        Assert.Contains("Teams", labels);
        Assert.Contains("Games", labels);
        Assert.Contains("Data", labels);
    }

    [Fact]
    public void A_guest_cannot_edit_and_cannot_see_admin_areas()
    {
        Assert.False(TripPermissions.Guest.CanEdit);
        Assert.False(TripPermissions.Guest.CanSeeAdminAreas);
    }

    [Theory]
    [InlineData("/itinerary?tab=travel", "/itinerary?tab=travel")]
    [InlineData("/teams", "/teams")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("https://not-us.example/phish", "/")]
    [InlineData("//not-us.example/phish", "/")]
    [InlineData("/\\not-us.example", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("teams", "/")]
    public void A_return_url_only_ever_points_back_at_this_site(string? given, string expected)
    {
        Assert.Equal(expected, SafeRedirect.Local(given));
    }
}
