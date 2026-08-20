namespace CharterTrip.Core.Models;

public sealed class ItineraryDay
{
    public string Id { get; set; } = "";
    public string Day { get; set; } = "";
    public string Date { get; set; } = "";
    public List<ItineraryItem> Items { get; set; } = [];
}

public sealed class ItineraryItem
{
    public string Id { get; set; } = "";

    /// <summary>
    /// Free text on purpose — people write "4:00 PM", but also "after dinner" and "TBD".
    /// TimeText.ToMinutes parses what it can for sorting and shrugs at the rest.
    /// </summary>
    public string Time { get; set; } = "";

    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public ItineraryTag Tag { get; set; } = ItineraryTag.Logistics;
}

public enum ItineraryTag
{
    Food,
    Game,
    Logistics,
    FreeTime
}
