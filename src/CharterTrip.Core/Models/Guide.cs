namespace CharterTrip.Core.Models;

/// <summary>
/// The guest handbook: the things people ask before they set off, and the things they forget to
/// pack.
///
/// This is the content of the printed guest itinerary, moved onto the site so there is one place
/// to look. It lives in trip.json like everything else — so it travels with an import, survives a
/// deploy, and can be corrected without shipping code — rather than being written into the page.
/// </summary>
public sealed class GuestGuide
{
    /// <summary>The where/when/what-is-covered table. Order is the order it is shown in.</summary>
    public List<GuideFact> Essentials { get; set; } = [];

    /// <summary>What Saturday evening actually is, in a sentence or two.</summary>
    public string SaturdayNight { get; set; } = "";

    /// <summary>What to wear for it. Kept separate because it is the part people act on.</summary>
    public string DressCode { get; set; } = "";

    /// <summary>The menu as a board: one card per day, three meal slots each.</summary>
    public List<MenuDay> MenuDays { get; set; } = [];

    /// <summary>The all-weekend spread — water, soda, snacks — as add/removable cards.</summary>
    public List<MenuStaple> Staples { get; set; } = [];

    /// <summary>The line under the menu — allergies, who to tell, that sort of thing.</summary>
    public string MenuNote { get; set; } = "";

    public List<PackList> Packing { get; set; } = [];

    /// <summary>What each car is on the hook for once the groups are settled.</summary>
    public List<string> CarBrings { get; set; } = [];

    public string CarBringsNote { get; set; } = "";
}

/// <summary>One row of the essentials table.</summary>
public sealed class GuideFact
{
    /// <summary>
    /// The heading this sits under — Logistics, Financial, and so on. Facts sharing a group are
    /// rendered together, in the order they appear in the list, so moving a row between headings
    /// is a matter of changing this rather than re-sorting anything.
    /// </summary>
    public string Group { get; set; } = "";

    public string Label { get; set; } = "";
    public string Value { get; set; } = "";

    /// <summary>
    /// Draws the eye to it. Exists for "Not covered" — the one row where skimming past it costs
    /// somebody a beer run, so it is the one row allowed to shout.
    /// </summary>
    public bool Highlight { get; set; }
}

/// <summary>
/// One day of the menu: a card with a slot per meal.
///
/// A slot is a plain string rather than a list of dishes because that is how the menu is
/// actually written — "Cookout — marinated skirt steak, tortillas, salsa" is one line, not five
/// records. Empty means nothing planned, and the board renders it as a place to put something.
/// </summary>
public sealed class MenuDay
{
    public string Id { get; set; } = "";
    public string Day { get; set; } = "";

    public string Breakfast { get; set; } = "";
    public string Lunch { get; set; } = "";
    public string Dinner { get; set; } = "";
}

/// <summary>One thing that is simply around all weekend: bottled water, soda, snacks.</summary>
public sealed class MenuStaple
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>One headed group of the packing list, such as Clothing or Toiletries.</summary>
public sealed class PackList
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Marks the group nobody may skip. Rendered ahead of the rest and called out.</summary>
    public bool Required { get; set; }

    public List<PackItem> Items { get; set; } = [];
}

/// <summary>
/// One line of the packing list.
///
/// The id is what a ticked box is remembered against, so it is written into the seed by hand and
/// never generated. A generated id would be different in every environment, and everyone's list
/// would empty itself the first time the trip was imported anywhere.
/// </summary>
public sealed class PackItem
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}
