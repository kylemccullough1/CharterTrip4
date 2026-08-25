namespace CharterTrip.Web.Auth;

public sealed record NavEntry(
    string Label,
    string Href,
    bool AdminOnly = false,
    IReadOnlyList<NavEntry>? Children = null)
{
    public bool HasChildren => Children is { Count: > 0 };
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
        new("Itinerary", "/itinerary"),
        new("Teams", "/teams"),
        new("Games", "/games", Children:
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
