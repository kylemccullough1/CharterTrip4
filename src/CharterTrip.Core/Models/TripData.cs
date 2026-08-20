namespace CharterTrip.Core.Models;

/// <summary>
/// The entire trip, in one object. This is what gets serialized to trip.json.
/// Everything hangs off here — there is exactly one instance of this alive in the
/// server at a time, held by ITripStore.
/// </summary>
public sealed class TripData
{
    /// <summary>Bumped on every mutation. Useful for debugging and for "did anything change?" checks.</summary>
    public int Revision { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public TripInfo Trip { get; set; } = new();
    public VenueInfo Venue { get; set; } = new();

    public List<CarouselSlide> Slides { get; set; } = [];
    public List<Team> Teams { get; set; } = [];
    public List<RosterPerson> Roster { get; set; } = [];
    public List<ItineraryDay> Itinerary { get; set; } = [];
    public List<Game> Games { get; set; } = [];
    public List<ScoreEntry> Scores { get; set; } = [];
    public List<Superlative> Superlatives { get; set; } = [];
    public JeopardyBoard Jeopardy { get; set; } = new();
    public MysteryState Mystery { get; set; } = new();
    public List<ChecklistGroup> Checklist { get; set; } = [];
    public List<BringItem> ToBring { get; set; } = [];
    public List<BudgetLine> Budget { get; set; } = [];
    public List<ShoppingList> Shopping { get; set; } = [];
}

public sealed class TripInfo
{
    public string Name { get; set; } = "";
    public string Kicker { get; set; } = "";
    public int Year { get; set; }
    public string Venue { get; set; } = "";
    public string City { get; set; } = "";
    public string Dates { get; set; } = "";
    public string Tagline { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    public decimal PricePerPerson { get; set; }
    public decimal HousingPerPerson { get; set; }
    public decimal FoodPerPerson { get; set; }
    public string Venmo { get; set; } = "";
    public List<string> Committee { get; set; } = [];
}

public sealed class VenueInfo
{
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string CheckIn { get; set; } = "";
    public string CheckOut { get; set; } = "";
    public string Byo { get; set; } = "";
    public List<string> Inside { get; set; } = [];
    public List<string> Outside { get; set; } = [];
    public List<string> NotIncluded { get; set; } = [];
    public List<NearbyPlace> Stores { get; set; } = [];
    public List<NearbyPlace> Parks { get; set; } = [];
}

public sealed class NearbyPlace
{
    public string Name { get; set; } = "";
    public string Distance { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class CarouselSlide
{
    public string Id { get; set; } = "";
    /// <summary>"deco" for a generated Art Deco placeholder, "photo" for an uploaded image.</summary>
    public string Kind { get; set; } = "deco";
    public string Caption { get; set; } = "";
    public string Sub { get; set; } = "";
    public int Hue { get; set; }
    /// <summary>Set when Kind == "photo". Resolved by IPhotoStore.</summary>
    public string? PhotoId { get; set; }
}
