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
    /// <summary>Where a new or repaired item lands: midday.</summary>
    public const int DefaultStartMinutes = 12 * 60;

    public string Id { get; set; } = "";

    /// <summary>
    /// The serialized start time. Nullable only so that a hand-edited or pre-v3 file degrades
    /// to a sensible default instead of failing to parse and taking the whole document with it.
    /// Read and write <see cref="StartMinutes"/> instead of this.
    /// </summary>
    [JsonPropertyName("startMinutes")]
    public int? StartMinutesOrNull { get; set; }

    /// <summary>
    /// When this starts, on the same scale <see cref="Services.TimeText.ToMinutes"/> produces:
    /// minutes past midnight, with anything before 6am shifted +1440 so a 12:00 AM nightcap
    /// belongs to the end of Saturday rather than the start of it.
    /// </summary>
    [JsonIgnore]
    public int StartMinutes
    {
        get => StartMinutesOrNull ?? DefaultStartMinutes;
        set => StartMinutesOrNull = value;
    }

    public int DurationMinutes { get; set; } = 60;

    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public ItineraryTag Tag { get; set; } = ItineraryTag.Logistics;

    /// <summary>
    /// Bumped on every change to this item. The editor records the version it opened with and
    /// refuses to save over a newer one, so two people editing the same thing get a choice
    /// instead of one of them silently losing their work.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// The pre-v2 free-text time field. Only the migration reads it; it is nulled out afterwards
    /// so WhenWritingNull drops it and trip.json cleans itself up on the next save.
    /// </summary>
    [JsonPropertyName("time")]
    public string? LegacyTime { get; set; }

    /// <summary>The pre-v3 home for times that could not be parsed. Folded into Notes by v3.</summary>
    [JsonPropertyName("timeNote")]
    public string? LegacyTimeNote { get; set; }

    [JsonIgnore]
    public int EndMinutes => StartMinutes + DurationMinutes;
}

public enum ItineraryTag
{
    Food,
    Game,
    Logistics,
    FreeTime
}

/// <summary>
/// One editor session's worth of changes, carried from the form to the store as a unit.
/// <paramref name="BaseVersion"/> is the item's version when the form was opened.
/// </summary>
public sealed record ItemEdit(
    string ItemId,
    int BaseVersion,
    string DayId,
    string Title,
    string Notes,
    ItineraryTag Tag,
    int StartMinutes,
    int DurationMinutes);

public enum SaveOutcome
{
    Saved,
    /// <summary>Someone else changed this item since the form was opened.</summary>
    Conflict,
    /// <summary>Someone else deleted it while the form was open.</summary>
    Missing
}
