using System.Text.Json.Serialization;

namespace CharterTrip.Core.Models;

public sealed class ItineraryDay
{
    public string Id { get; set; } = "";
    public string Day { get; set; } = "";
    public string Date { get; set; } = "";
    public List<ItineraryItem> Items { get; set; } = [];

    /// <summary>
    /// Optional fixed bounds for the planner grid. Null means "fit to whatever is scheduled",
    /// which is almost always what you want — an empty Sunday shouldn't render 18 blank hours.
    /// </summary>
    public int? WindowStartMinutes { get; set; }
    public int? WindowEndMinutes { get; set; }
}

public sealed class ItineraryItem
{
    public string Id { get; set; } = "";

    /// <summary>
    /// When this starts, on the same scale <see cref="Services.TimeText.ToMinutes"/> produces:
    /// minutes past midnight, with anything before 6am shifted +1440 so a 12:00 AM nightcap
    /// belongs to the end of Saturday rather than the start of it.
    /// Null means unscheduled — it sits in the tray until someone gives it a time.
    /// </summary>
    public int? StartMinutes { get; set; }

    public int DurationMinutes { get; set; } = 60;

    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public ItineraryTag Tag { get; set; } = ItineraryTag.Logistics;

    /// <summary>What the time used to say when it wasn't a clock time — "after dinner", "TBD".</summary>
    public string? TimeNote { get; set; }

    /// <summary>
    /// The pre-v2 free-text time field. Only the migration reads it; it is nulled out afterwards
    /// so WhenWritingNull drops it and trip.json cleans itself up on the next save.
    /// </summary>
    [JsonPropertyName("time")]
    public string? LegacyTime { get; set; }

    [JsonIgnore]
    public bool IsScheduled => StartMinutes.HasValue;

    [JsonIgnore]
    public int EndMinutes => (StartMinutes ?? 0) + DurationMinutes;
}

public enum ItineraryTag
{
    Food,
    Game,
    Logistics,
    FreeTime
}
