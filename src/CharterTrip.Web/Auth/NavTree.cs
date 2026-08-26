namespace CharterTrip.Web.Auth;

public sealed record NavEntry(
    string Label,
    string Href,
    bool AdminOnly = false,
    IReadOnlyList<NavEntry>? Children = null,
    string? LandsOn = null)
{
    public bool HasChildren => Children is { Count: > 0 };

    /// <summary>
    /// Where the link actually points. <see cref="Href"/> stays the address the highlight is
    /// judged against, and <see cref="LandsOn"/> names a card to scroll to without narrowing
    /// which pages count as this entry.
    ///
    /// The two are not the same thing, and no rule can tell them apart from the URL alone:
    /// /itinerary#guide means "the itinerary page, and start me at the top", while
    /// /itinerary#packing means "the packing section" and is current nowhere else. Identical
    /// shape, opposite intent — so the intent is declared here rather than inferred.
    /// </summary>
    public string LinkHref => LandsOn is null ? Href : $"{Href}#{LandsOn}";
}

/// <summary>
/// The navigation, in one place, marked up with who is allowed to see what. Both shells read
/// from this, so adding a page means adding one line here rather than editing two menus.
/// </summary>
public static class NavTree
{
    public static readonly IReadOnlyList<NavEntry> All =
    [
        new("Home", "/"),
        // The itinerary page is four tabs and a packing card, and until now the menu offered
        // no way in but the front door. The children are its sections: the tabs go through the
        // ?tab= query the page already reads, and "What to bring" is an anchor because that card
        // is not a tab — it sits below them all.
        //
        // Every one of them names where to land as well as what to show. Switching tab is a
        // same-page move that Blazor does not reset the scroll for, so coming back up from the
        // packing card used to change the tab a thousand pixels above the fold and look for all
        // the world like a dead menu. #guide is the tabbed card; #packing is the one below it.
        new("Itinerary", "/itinerary", LandsOn: "guide", Children:
        [
            new("Essentials", "/itinerary?tab=essentials#guide"),
            new("Schedule", "/itinerary?tab=schedule#guide"),
            new("Menu", "/itinerary?tab=menu#guide"),
            new("Carpool", "/itinerary?tab=carpool#guide"),
            new("What to bring", "/itinerary#packing")
        ]),
        new("Teams", "/teams", AdminOnly: true),
        new("Games", "/games", AdminOnly: true, Children:
        [
            new("All games", "/games"),
            new("Jeopardy", "/games/jeopardy"),
            new("Newlywed Game", "/games/newlywed"),
            new("Police Sketch", "/games/sketch"),
            new("Spelling Bee", "/games/spelling"),
            new("Pool Noodle Cups", "/games/noodlecup"),
            new("Beer Run", "/games/beerrun"),
            new("Relay Race", "/games/relay"),
            new("Superlatives", "/games/superlatives"),
            new("Murder Mystery", "/mystery")
        ]),
        new("Venue", "/venue"),
        new("Data", "/admin/import", AdminOnly: true)
    ];

    /// <summary>
    /// Whether an entry is the page you are looking at, given the current path with its query
    /// and fragment — "itinerary?tab=menu", as <c>NavigationManager.ToBaseRelativePath</c>
    /// hands it over.
    ///
    /// This is here rather than left to <c>NavLink</c> because NavLink stopped being able to
    /// answer it: since .NET 9 it matches on the path alone, and every section of the itinerary
    /// shares one path, so all five lit up together. An href that names a tab or an anchor is
    /// therefore matched exactly — for those the query IS the destination — while a plain path
    /// still covers everything beneath it, so Games stays lit on /games/jeopardy.
    /// </summary>
    public static bool IsCurrent(string href, string current)
    {
        href = Normalise(href);
        current = Normalise(current);

        if (string.Equals(href, current, StringComparison.OrdinalIgnoreCase)) return true;
        if (href.Contains('?') || href.Contains('#')) return false;
        if (href == "/") return false;   // otherwise Home is the prefix of the whole site

        return current.Length > href.Length
            && current.StartsWith(href, StringComparison.OrdinalIgnoreCase)
            && current[href.Length] is '/' or '?' or '#';
    }

    /// <summary>One leading slash, no trailing one, so the two sides can be compared as text.</summary>
    private static string Normalise(string value)
    {
        var v = value.Trim();
        if (!v.StartsWith('/')) v = "/" + v;
        return v.Length > 1 ? v.TrimEnd('/') : v;
    }

    /// <summary>The menu as a given person should see it, with admin-only branches pruned.</summary>
    public static IReadOnlyList<NavEntry> For(TripPermissions permissions)
    {
        if (permissions.CanSeeAdminAreas) return All;

        return All
            .Where(e => !e.AdminOnly)
            .Select(e => e.HasChildren
                ? e with { Children = e.Children!.Where(c => !c.AdminOnly).ToList() }
                : e)
            .Where(e => !e.HasChildren || e.Children!.Count > 0)
            .ToList();
    }
}
