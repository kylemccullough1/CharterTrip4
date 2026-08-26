using CharterTrip.Web.Auth;

namespace CharterTrip.Tests;

/// <summary>
/// Which menu entry is lit. The itinerary's sections all share one path and differ only in the
/// query, which is precisely what NavLink stopped looking at, so the rule is ours to get right.
/// </summary>
public class NavHighlightTests
{
    [Theory]
    [InlineData("/itinerary?tab=schedule#guide", "itinerary?tab=schedule#guide", true)]
    [InlineData("/itinerary?tab=schedule#guide", "itinerary?tab=menu#guide", false)]
    [InlineData("/itinerary?tab=carpool#guide", "itinerary?tab=carpool#guide", true)]
    [InlineData("/itinerary?tab=carpool#guide", "itinerary?tab=travel#guide", false)]
    [InlineData("/itinerary?tab=schedule#guide", "itinerary", false)]
    [InlineData("/itinerary?tab=schedule#guide", "itinerary#packing", false)]
    [InlineData("/itinerary#packing", "itinerary#packing", true)]
    [InlineData("/itinerary#packing", "itinerary?tab=menu#guide", false)]
    [InlineData("/itinerary#packing", "itinerary", false)]
    public void A_section_is_lit_only_where_it_actually_is(string href, string current, bool expected)
    {
        Assert.Equal(expected, NavTree.IsCurrent(href, current));
    }

    [Theory]
    [InlineData("/itinerary", "itinerary", true)]
    [InlineData("/itinerary", "itinerary?tab=menu#guide", true)]
    [InlineData("/itinerary", "itinerary#packing", true)]
    [InlineData("/games", "games/jeopardy", true)]
    [InlineData("/games", "games", true)]
    [InlineData("/games", "teams", false)]
    public void A_parent_covers_everything_underneath_it(string href, string current, bool expected)
    {
        Assert.Equal(expected, NavTree.IsCurrent(href, current));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("itinerary", false)]
    [InlineData("games/jeopardy", false)]
    public void Home_is_the_home_page_and_not_the_whole_site(string current, bool expected)
    {
        Assert.Equal(expected, NavTree.IsCurrent("/", current));
    }

    /// <summary>
    /// Every section says where to land, not only what to show. Without it, switching tab from
    /// the packing card left the reader parked below the fold looking at an unchanged screen.
    /// </summary>
    [Fact]
    public void Every_itinerary_section_names_the_card_it_scrolls_to()
    {
        var itinerary = NavTree.All.Single(e => e.Label == "Itinerary");

        Assert.Contains('#', itinerary.LinkHref);
        Assert.All(itinerary.Children!, c => Assert.Contains('#', c.LinkHref));
    }

    /// <summary>
    /// The parent lands at the top of the page without that landing spot narrowing what it
    /// matches — it stays lit on every section underneath it.
    /// </summary>
    [Fact]
    public void Where_the_parent_lands_is_not_what_the_parent_matches()
    {
        var itinerary = NavTree.All.Single(e => e.Label == "Itinerary");

        Assert.Equal("/itinerary#guide", itinerary.LinkHref);
        Assert.Equal("/itinerary", itinerary.Href);
        Assert.True(NavTree.IsCurrent(itinerary.Href, "itinerary?tab=menu#guide"));
        Assert.True(NavTree.IsCurrent(itinerary.Href, "itinerary#packing"));
    }

    /// <summary>The tab the committee renamed is the one the menu offers.</summary>
    [Fact]
    public void The_itinerary_offers_carpool_rather_than_travel()
    {
        var sections = NavTree.All.Single(e => e.Label == "Itinerary").Children!;

        Assert.Contains(sections, c => c.Label == "Carpool");
        Assert.DoesNotContain(sections, c => c.Label == "Travel");
        Assert.Equal("/itinerary?tab=carpool#guide", sections.Single(c => c.Label == "Carpool").LinkHref);
    }

    [Fact]
    public void An_entry_with_nowhere_to_land_links_to_its_own_address()
    {
        var venue = NavTree.All.Single(e => e.Label == "Venue");

        Assert.Equal(venue.Href, venue.LinkHref);
    }

    [Fact]
    public void Every_itinerary_section_in_the_menu_lights_up_on_its_own_and_alone()
    {
        var itinerary = NavTree.All.Single(e => e.Label == "Itinerary");

        foreach (var section in itinerary.Children!)
        {
            var here = section.Href.TrimStart('/');
            var lit = itinerary.Children!.Where(c => NavTree.IsCurrent(c.Href, here)).ToList();

            Assert.Equal([section.Label], lit.Select(c => c.Label));
        }
    }
}
