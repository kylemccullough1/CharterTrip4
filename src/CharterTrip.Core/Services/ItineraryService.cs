using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>
/// All itinerary reordering logic, deliberately kept out of the Blazor component so it can
/// be unit tested without a browser. Every method mutates the TripData it is handed — callers
/// run them inside ITripStore.MutateAsync, which owns the locking and persistence.
/// </summary>
public static class ItineraryService
{
    public static ItineraryDay AddDay(TripData trip, string name = "New day", string date = "")
    {
        var day = new ItineraryDay { Id = Ids.New("day"), Day = name, Date = date };
        trip.Itinerary.Add(day);
        return day;
    }

    public static void RemoveDay(TripData trip, string dayId) =>
        trip.Itinerary.RemoveAll(d => d.Id == dayId);

    public static ItineraryItem? AddItem(TripData trip, string dayId)
    {
        var day = FindDay(trip, dayId);
        if (day is null) return null;

        var item = new ItineraryItem
        {
            Id = Ids.New("item"),
            Time = "12:00 PM",
            Title = "New item",
            Tag = ItineraryTag.Logistics
        };
        day.Items.Add(item);
        return item;
    }

    public static void RemoveItem(TripData trip, string itemId)
    {
        foreach (var day in trip.Itinerary)
            day.Items.RemoveAll(i => i.Id == itemId);
    }

    /// <summary>Move an item up (-1) or down (+1) within its own day. No-op at the ends.</summary>
    public static void Nudge(TripData trip, string itemId, int delta)
    {
        var (day, item) = Locate(trip, itemId);
        if (day is null || item is null) return;

        var from = day.Items.IndexOf(item);
        var to = from + delta;
        if (to < 0 || to >= day.Items.Count) return;

        day.Items.RemoveAt(from);
        day.Items.Insert(to, item);
    }

    /// <summary>Move an item to the previous (-1) or next (+1) day, landing at the end.</summary>
    public static void MoveToAdjacentDay(TripData trip, string itemId, int direction)
    {
        var (day, item) = Locate(trip, itemId);
        if (day is null || item is null) return;

        var target = trip.Itinerary.ElementAtOrDefault(trip.Itinerary.IndexOf(day) + direction);
        if (target is null) return;

        day.Items.Remove(item);
        target.Items.Add(item);
    }

    /// <summary>
    /// Drop an item into a specific day at a specific index. This is what a drag-and-drop
    /// reorder calls once the browser tells us where the card landed.
    /// </summary>
    public static void MoveItem(TripData trip, string itemId, string targetDayId, int targetIndex)
    {
        var (day, item) = Locate(trip, itemId);
        var target = FindDay(trip, targetDayId);
        if (day is null || item is null || target is null) return;

        day.Items.Remove(item);
        targetIndex = Math.Clamp(targetIndex, 0, target.Items.Count);
        target.Items.Insert(targetIndex, item);
    }

    public static void SortDayByTime(TripData trip, string dayId)
    {
        var day = FindDay(trip, dayId);
        if (day is null) return;

        // OrderBy is stable, so items sharing a time (or both unparseable) keep their order.
        day.Items = day.Items.OrderBy(i => TimeText.ToMinutes(i.Time)).ToList();
    }

    public static ItineraryDay? FindDay(TripData trip, string dayId) =>
        trip.Itinerary.FirstOrDefault(d => d.Id == dayId);

    public static (ItineraryDay? Day, ItineraryItem? Item) Locate(TripData trip, string itemId)
    {
        foreach (var day in trip.Itinerary)
        {
            var item = day.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not null) return (day, item);
        }
        return (null, null);
    }
}
